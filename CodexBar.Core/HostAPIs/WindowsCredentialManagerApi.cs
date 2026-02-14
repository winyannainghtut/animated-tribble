using System.Runtime.InteropServices;
using System.Text;

namespace CodexBar.Core.HostAPIs;

public sealed class WindowsCredentialManagerApi : ICredentialStore
{
    private const int CredTypeGeneric = 1;

    public Task<string?> ReadSecretAsync(string targetName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult<string?>(null);
        }

        if (!CredRead(targetName, CredTypeGeneric, 0, out var credentialPointer))
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPointer);
            if (credential.CredentialBlobSize <= 0 || credential.CredentialBlob == IntPtr.Zero)
            {
                return Task.FromResult<string?>(null);
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public Task WriteSecretAsync(string targetName, string secret, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return Task.CompletedTask;
        }

        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var blobPointer = Marshal.AllocHGlobal(secretBytes.Length);
        Marshal.Copy(secretBytes, 0, blobPointer, secretBytes.Length);

        try
        {
            var credential = new CREDENTIAL
            {
                Type = CredTypeGeneric,
                TargetName = targetName,
                CredentialBlobSize = (uint)secretBytes.Length,
                CredentialBlob = blobPointer,
                Persist = (uint)CredentialPersistence.LocalMachine,
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                Comment = null,
                UserName = "codexbar"
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException($"CredWrite failed for '{targetName}' with code {Marshal.GetLastWin32Error()}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blobPointer);
        }

        return Task.CompletedTask;
    }

    public Task DeleteSecretAsync(string targetName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (OperatingSystem.IsWindows())
        {
            CredDelete(targetName, CredTypeGeneric, 0);
        }

        return Task.CompletedTask;
    }

    private enum CredentialPersistence : uint
    {
        Session = 1,
        LocalMachine = 2,
        Enterprise = 3
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref CREDENTIAL userCredential, [In] uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("Advapi32.dll", SetLastError = true)]
    private static extern bool CredFree([In] IntPtr credential);
}
