using CodexBar.Core.HostAPIs;
using CodexBar.Core.Models;
using CodexBar.Core.Providers;

namespace CodexBar.Core.Services;

public sealed class RefreshCoordinator
{
    private readonly ProviderRegistry _registry;
    private readonly UsageFetcher _usageFetcher;
    private readonly UsageStore _usageStore;
    private readonly SettingsStore _settingsStore;
    private readonly IHttpApi _httpApi;
    private readonly ICredentialStore _credentialStore;
    private readonly IBrowserCookieApi _browserCookieApi;
    private readonly IPtyApi _ptyApi;
    private readonly IStatusApi _statusApi;

    public RefreshCoordinator(
        ProviderRegistry registry,
        UsageFetcher usageFetcher,
        UsageStore usageStore,
        SettingsStore settingsStore,
        IHttpApi httpApi,
        ICredentialStore credentialStore,
        IBrowserCookieApi browserCookieApi,
        IPtyApi ptyApi,
        IStatusApi statusApi)
    {
        _registry = registry;
        _usageFetcher = usageFetcher;
        _usageStore = usageStore;
        _settingsStore = settingsStore;
        _httpApi = httpApi;
        _credentialStore = credentialStore;
        _browserCookieApi = browserCookieApi;
        _ptyApi = ptyApi;
        _statusApi = statusApi;
    }

    public async Task<IReadOnlyList<ProviderFetchResult>> RefreshAsync(
        IEnumerable<ProviderId>? providerIds = null,
        bool respectSettings = true,
        bool verbose = false,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsStore
            .LoadAsync(_registry.Providers.Select(provider => provider.Id), cancellationToken)
            .ConfigureAwait(false);

        var selected = providerIds?.ToHashSet() ?? _registry.Providers.Select(provider => provider.Id).ToHashSet();
        var enabledProviders = _registry.Providers
            .Where(provider =>
                selected.Contains(provider.Id) &&
                (!respectSettings || settings.IsProviderEnabled(provider.Id, provider.DefaultEnabled)))
            .ToArray();

        var context = new ProviderFetchContext(
            HomeDirectory: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AppDataDirectory: Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            LocalAppDataDirectory: Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Settings: settings,
            HttpApi: _httpApi,
            CredentialStore: _credentialStore,
            BrowserCookieApi: _browserCookieApi,
            PtyApi: _ptyApi,
            StatusApi: _statusApi,
            NowUtc: DateTimeOffset.UtcNow,
            Verbose: verbose);

        var tasks = enabledProviders
            .Select(provider => RefreshProviderAsync(provider, context, settings.PollIncidents, cancellationToken))
            .ToArray();

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<ProviderFetchResult> RefreshProviderAsync(
        IProviderDescriptor provider,
        ProviderFetchContext context,
        bool pollIncidents,
        CancellationToken cancellationToken)
    {
        var result = await _usageFetcher.FetchAsync(provider, context, cancellationToken).ConfigureAwait(false);

        if (!result.Success || result.Usage is null)
        {
            return result;
        }

        var usage = result.Usage;
        if (provider.SupportsStatusPolling && pollIncidents)
        {
            var status = await _statusApi.GetStatusAsync(provider.Id, cancellationToken).ConfigureAwait(false);
            usage = usage with
            {
                IncidentSummary = status.HasIncident ? status.Summary : null,
                IsStale = usage.IsStale || status.HasIncident && status.Severity >= IncidentSeverity.Major
            };
        }

        _usageStore.Upsert(usage);

        return result with { Usage = usage };
    }

    public async Task RunAutoRefreshLoopAsync(Func<bool> shouldContinue, bool verbose = false, CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested && shouldContinue())
        {
            var settings = await _settingsStore
                .LoadAsync(_registry.Providers.Select(provider => provider.Id), cancellationToken)
                .ConfigureAwait(false);

            await RefreshAsync(verbose: verbose, cancellationToken: cancellationToken).ConfigureAwait(false);

            var interval = TimeSpan.FromMinutes(Math.Clamp(settings.RefreshIntervalMinutes, 1, 60));
            try
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
