namespace CodexBar.Core.HostAPIs;

public interface ICredentialStore
{
    Task<string?> ReadSecretAsync(string targetName, CancellationToken cancellationToken);
    Task WriteSecretAsync(string targetName, string secret, CancellationToken cancellationToken);
    Task DeleteSecretAsync(string targetName, CancellationToken cancellationToken);
}
