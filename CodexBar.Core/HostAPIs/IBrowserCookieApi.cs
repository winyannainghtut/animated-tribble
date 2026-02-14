namespace CodexBar.Core.HostAPIs;

public enum BrowserKind
{
    Any,
    Chrome,
    Edge,
    Brave,
    Firefox
}

public sealed record BrowserCookieQuery(
    IReadOnlyList<string> Domains,
    BrowserKind Browser,
    bool IncludeExpired = false
);

public sealed record BrowserCookie(
    string Domain,
    string Name,
    string Value,
    string Path,
    DateTimeOffset? ExpiresAtUtc,
    bool IsSecure,
    bool IsHttpOnly
);

public interface IBrowserCookieApi
{
    Task<IReadOnlyList<BrowserCookie>> ReadCookiesAsync(BrowserCookieQuery query, CancellationToken cancellationToken);
}
