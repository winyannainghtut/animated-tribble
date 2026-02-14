using CodexBar.Core.HostAPIs;

namespace CodexBar.Core.Models;

public enum FetchKind
{
    Cli,
    OAuth,
    Cookies,
    ApiToken,
    LocalProbe,
    Rpc
}

public sealed record UsageWindowSnapshot(
    string Label,
    int UsedPercent,
    DateTimeOffset? ResetAtUtc,
    TimeSpan? ResetsIn
);

public sealed record ProviderUsageSnapshot(
    ProviderId ProviderId,
    UsageWindowSnapshot Session,
    UsageWindowSnapshot Secondary,
    decimal? Credits,
    decimal? EstimatedTokenCostUsd,
    bool IsStale,
    DateTimeOffset FetchedAtUtc,
    string? IncidentSummary,
    IReadOnlyList<string> Notes
);

public sealed record FetchAttempt(
    string StrategyId,
    FetchKind Kind,
    bool Available,
    bool Succeeded,
    string Message,
    DateTimeOffset TimestampUtc
);

public sealed record ProviderFetchResult(
    ProviderId ProviderId,
    ProviderUsageSnapshot? Usage,
    bool Success,
    bool IsStale,
    string? Error,
    IReadOnlyList<FetchAttempt> Attempts
)
{
    public static ProviderFetchResult Failed(ProviderId providerId, string error, IReadOnlyList<FetchAttempt>? attempts = null)
        => new(providerId, null, false, true, error, attempts ?? Array.Empty<FetchAttempt>());

    public static ProviderFetchResult FromUsage(ProviderUsageSnapshot usage, IReadOnlyList<FetchAttempt> attempts)
        => new(usage.ProviderId, usage, true, usage.IsStale, null, attempts);
}

public sealed record ProviderFetchContext(
    string HomeDirectory,
    string AppDataDirectory,
    string LocalAppDataDirectory,
    AppSettings Settings,
    IHttpApi HttpApi,
    ICredentialStore CredentialStore,
    IBrowserCookieApi BrowserCookieApi,
    IPtyApi PtyApi,
    IStatusApi StatusApi,
    DateTimeOffset NowUtc,
    bool Verbose
);
