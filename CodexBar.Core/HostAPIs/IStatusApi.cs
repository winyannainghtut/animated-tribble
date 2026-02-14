using CodexBar.Core.Models;

namespace CodexBar.Core.HostAPIs;

public enum IncidentSeverity
{
    None,
    Minor,
    Major,
    Critical
}

public sealed record ProviderIncidentStatus(
    ProviderId ProviderId,
    bool HasIncident,
    IncidentSeverity Severity,
    string? Summary,
    DateTimeOffset CheckedAtUtc
);

public interface IStatusApi
{
    Task<ProviderIncidentStatus> GetStatusAsync(ProviderId providerId, CancellationToken cancellationToken);
}
