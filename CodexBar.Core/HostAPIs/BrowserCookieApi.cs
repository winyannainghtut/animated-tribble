using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CodexBar.Core.HostAPIs;

public sealed class BrowserCookieApi : IBrowserCookieApi
{
    public async Task<IReadOnlyList<BrowserCookie>> ReadCookiesAsync(BrowserCookieQuery query, CancellationToken cancellationToken)
    {
        if (query.Domains.Count == 0)
        {
            return Array.Empty<BrowserCookie>();
        }

        var normalizedDomains = query.Domains
            .Select(NormalizeDomain)
            .Where(static domain => !string.IsNullOrWhiteSpace(domain))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedDomains.Length == 0)
        {
            return Array.Empty<BrowserCookie>();
        }

        var cookies = new List<BrowserCookie>();

        if (query.Browser is BrowserKind.Any or BrowserKind.Chrome)
        {
            cookies.AddRange(await ReadChromiumCookiesAsync(
                userDataRoot: Path.Combine(GetLocalAppData(), "Google", "Chrome", "User Data"),
                normalizedDomains,
                cancellationToken).ConfigureAwait(false));
        }

        if (query.Browser is BrowserKind.Any or BrowserKind.Edge)
        {
            cookies.AddRange(await ReadChromiumCookiesAsync(
                userDataRoot: Path.Combine(GetLocalAppData(), "Microsoft", "Edge", "User Data"),
                normalizedDomains,
                cancellationToken).ConfigureAwait(false));
        }

        if (query.Browser is BrowserKind.Any or BrowserKind.Brave)
        {
            cookies.AddRange(await ReadChromiumCookiesAsync(
                userDataRoot: Path.Combine(GetLocalAppData(), "BraveSoftware", "Brave-Browser", "User Data"),
                normalizedDomains,
                cancellationToken).ConfigureAwait(false));
        }

        if (query.Browser is BrowserKind.Any or BrowserKind.Firefox)
        {
            cookies.AddRange(await ReadFirefoxCookiesAsync(normalizedDomains, cancellationToken).ConfigureAwait(false));
        }

        var now = DateTimeOffset.UtcNow;
        var deduped = cookies
            .Where(cookie => query.IncludeExpired || cookie.ExpiresAtUtc is null || cookie.ExpiresAtUtc > now)
            .GroupBy(cookie => $"{cookie.Domain}|{cookie.Name}|{cookie.Path}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(cookie => cookie.ExpiresAtUtc ?? DateTimeOffset.MaxValue).First())
            .ToArray();

        return deduped;
    }

    private static async Task<IReadOnlyList<BrowserCookie>> ReadChromiumCookiesAsync(
        string userDataRoot,
        IReadOnlyList<string> domains,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(userDataRoot))
        {
            return Array.Empty<BrowserCookie>();
        }

        var masterKey = await TryGetChromiumMasterKeyAsync(userDataRoot, cancellationToken).ConfigureAwait(false);
        var dbFiles = Directory
            .EnumerateDirectories(userDataRoot)
            .Select(profileDir => Path.Combine(profileDir, "Network", "Cookies"))
            .Where(File.Exists)
            .ToArray();

        var result = new List<BrowserCookie>();

        foreach (var dbFile in dbFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tempCopy = CopyToTemporaryLocation(dbFile);
            try
            {
                result.AddRange(await ReadChromiumCookieDbAsync(tempCopy, domains, masterKey, cancellationToken).ConfigureAwait(false));
            }
            finally
            {
                TryDeleteFile(tempCopy);
            }
        }

        return result;
    }

    private static async Task<IReadOnlyList<BrowserCookie>> ReadChromiumCookieDbAsync(
        string dbFile,
        IReadOnlyList<string> domains,
        byte[]? masterKey,
        CancellationToken cancellationToken)
    {
        var result = new List<BrowserCookie>();

        await using var connection = new SqliteConnection($"Data Source={dbFile};Mode=ReadOnly");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        foreach (var domain in domains)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT host_key, name, path, expires_utc, is_secure, is_httponly, encrypted_value, value
FROM cookies
WHERE host_key = $domain
   OR host_key = $dotDomain
   OR host_key LIKE $subdomainPattern";
            command.Parameters.AddWithValue("$domain", domain);
            command.Parameters.AddWithValue("$dotDomain", $".{domain}");
            command.Parameters.AddWithValue("$subdomainPattern", $"%.{domain}");

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var encryptedValue = reader[6] as byte[];
                var value = reader.IsDBNull(7) ? string.Empty : reader.GetString(7);
                if (string.IsNullOrEmpty(value) && encryptedValue is not null)
                {
                    value = DecryptChromiumCookieValue(encryptedValue, masterKey) ?? string.Empty;
                }

                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                var host = reader.GetString(0);
                if (!IsHostMatchForDomain(host, domain))
                {
                    continue;
                }

                result.Add(new BrowserCookie(
                    Domain: host,
                    Name: reader.GetString(1),
                    Value: value,
                    Path: reader.IsDBNull(2) ? "/" : reader.GetString(2),
                    ExpiresAtUtc: ConvertChromiumExpiry(reader.IsDBNull(3) ? 0L : reader.GetInt64(3)),
                    IsSecure: !reader.IsDBNull(4) && reader.GetBoolean(4),
                    IsHttpOnly: !reader.IsDBNull(5) && reader.GetBoolean(5)
                ));
            }
        }

        return result;
    }

    private static async Task<IReadOnlyList<BrowserCookie>> ReadFirefoxCookiesAsync(IReadOnlyList<string> domains, CancellationToken cancellationToken)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var profilesRoot = Path.Combine(appData, "Mozilla", "Firefox", "Profiles");

        if (!Directory.Exists(profilesRoot))
        {
            return Array.Empty<BrowserCookie>();
        }

        var result = new List<BrowserCookie>();
        var cookieDbs = Directory
            .EnumerateDirectories(profilesRoot)
            .Select(profile => Path.Combine(profile, "cookies.sqlite"))
            .Where(File.Exists)
            .ToArray();

        foreach (var cookieDb in cookieDbs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tempCopy = CopyToTemporaryLocation(cookieDb);
            try
            {
                await using var connection = new SqliteConnection($"Data Source={tempCopy};Mode=ReadOnly");
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                foreach (var domain in domains)
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = @"
SELECT host, name, path, expiry, isSecure, isHttpOnly, value
FROM moz_cookies
WHERE host = $domain
   OR host = $dotDomain
   OR host LIKE $subdomainPattern";
                    command.Parameters.AddWithValue("$domain", domain);
                    command.Parameters.AddWithValue("$dotDomain", $".{domain}");
                    command.Parameters.AddWithValue("$subdomainPattern", $"%.{domain}");

                    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var expires = reader.IsDBNull(3) ? (DateTimeOffset?)null : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(3));
                        var value = reader.IsDBNull(6) ? string.Empty : reader.GetString(6);
                        if (string.IsNullOrEmpty(value))
                        {
                            continue;
                        }

                        var host = reader.GetString(0);
                        if (!IsHostMatchForDomain(host, domain))
                        {
                            continue;
                        }

                        result.Add(new BrowserCookie(
                            Domain: host,
                            Name: reader.GetString(1),
                            Value: value,
                            Path: reader.IsDBNull(2) ? "/" : reader.GetString(2),
                            ExpiresAtUtc: expires,
                            IsSecure: !reader.IsDBNull(4) && reader.GetBoolean(4),
                            IsHttpOnly: !reader.IsDBNull(5) && reader.GetBoolean(5)
                        ));
                    }
                }
            }
            finally
            {
                TryDeleteFile(tempCopy);
            }
        }

        return result;
    }

    private static string GetLocalAppData()
        => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static string CopyToTemporaryLocation(string dbFile)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"codexbar-{Guid.NewGuid():N}.sqlite");
        File.Copy(dbFile, tempFile, overwrite: true);
        return tempFile;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static DateTimeOffset? ConvertChromiumExpiry(long rawMicroseconds)
    {
        if (rawMicroseconds <= 0)
        {
            return null;
        }

        try
        {
            var epoch = new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero);
            return epoch.AddTicks(rawMicroseconds * 10);
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return null;
        }

        var normalized = domain.Trim().Trim('.').ToLowerInvariant();
        return normalized.Length == 0 ? null : normalized;
    }

    private static bool IsHostMatchForDomain(string host, string normalizedDomain)
    {
        var normalizedHost = NormalizeDomain(host);
        if (normalizedHost is null)
        {
            return false;
        }

        return normalizedHost.Equals(normalizedDomain, StringComparison.Ordinal) ||
               normalizedHost.EndsWith($".{normalizedDomain}", StringComparison.Ordinal);
    }

    private static async Task<byte[]?> TryGetChromiumMasterKeyAsync(string userDataRoot, CancellationToken cancellationToken)
    {
        var localStatePath = Path.Combine(userDataRoot, "Local State");
        if (!File.Exists(localStatePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(localStatePath);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!json.RootElement.TryGetProperty("os_crypt", out var osCrypt) ||
            !osCrypt.TryGetProperty("encrypted_key", out var encryptedKeyProperty))
        {
            return null;
        }

        var encoded = encryptedKeyProperty.GetString();
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return null;
        }

        var encrypted = Convert.FromBase64String(encoded);
        var stripped = encrypted.AsSpan().StartsWith("DPAPI"u8)
            ? encrypted[5..]
            : encrypted;

        return DecryptDpapi(stripped);
    }

    private static string? DecryptChromiumCookieValue(byte[] encryptedValue, byte[]? masterKey)
    {
        try
        {
            if (encryptedValue.Length > 3 &&
                encryptedValue[0] == (byte)'v' &&
                encryptedValue[1] == (byte)'1' &&
                (encryptedValue[2] == (byte)'0' || encryptedValue[2] == (byte)'1') &&
                masterKey is not null)
            {
                var nonce = encryptedValue[3..15];
                var cipherTag = encryptedValue[15..];
                var cipher = cipherTag[..^16];
                var tag = cipherTag[^16..];

                var plaintext = new byte[cipher.Length];
                using var aes = new AesGcm(masterKey, tag.Length);
                aes.Decrypt(nonce, cipher, tag, plaintext);
                return Encoding.UTF8.GetString(plaintext);
            }

            var dpapi = DecryptDpapi(encryptedValue);
            return dpapi is null ? null : Encoding.UTF8.GetString(dpapi);
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? DecryptDpapi(byte[] encrypted)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var dataIn = new DATA_BLOB();
        var dataOut = new DATA_BLOB();

        try
        {
            dataIn.pbData = Marshal.AllocHGlobal(encrypted.Length);
            dataIn.cbData = encrypted.Length;
            Marshal.Copy(encrypted, 0, dataIn.pbData, encrypted.Length);

            if (!CryptUnprotectData(ref dataIn, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref dataOut))
            {
                return null;
            }

            var decrypted = new byte[dataOut.cbData];
            Marshal.Copy(dataOut.pbData, decrypted, 0, dataOut.cbData);
            return decrypted;
        }
        finally
        {
            if (dataIn.pbData != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(dataIn.pbData);
            }

            if (dataOut.pbData != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(dataOut.pbData);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool CryptUnprotectData(
        ref DATA_BLOB pDataIn,
        IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        ref DATA_BLOB pDataOut);
}
