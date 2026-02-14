namespace CodexBar.Core.Models;

public sealed class AppSettings
{
    public Dictionary<ProviderId, bool> ProviderEnabled { get; init; } = new();
    public int RefreshIntervalMinutes { get; init; } = 5;
    public bool MergeIcons { get; init; } = false;
    public bool ShowUsageAsUsed { get; init; } = true;
    public ProviderId ActiveProviderInMergeMode { get; init; } = ProviderId.Codex;
    public bool PollIncidents { get; init; } = true;

    public bool IsProviderEnabled(ProviderId providerId, bool providerDefault)
        => ProviderEnabled.TryGetValue(providerId, out var enabled) ? enabled : providerDefault;
}

public sealed record AppRuntimeState(
    DateTimeOffset LastRefreshUtc,
    bool LastRefreshSucceeded,
    string? LastError
);
