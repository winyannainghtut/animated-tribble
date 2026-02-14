using CodexBar.Core.HostAPIs;
using CodexBar.Core.Providers;

namespace CodexBar.Core.Services;

public sealed record CoreRuntime(
    ProviderRegistry ProviderRegistry,
    UsageFetcher UsageFetcher,
    UsageStore UsageStore,
    SettingsStore SettingsStore,
    RefreshCoordinator RefreshCoordinator,
    CodexCostScanner CodexCostScanner,
    IHttpApi HttpApi,
    ICredentialStore CredentialStore,
    IBrowserCookieApi BrowserCookieApi,
    IPtyApi PtyApi,
    IStatusApi StatusApi
);

public static class CoreRuntimeFactory
{
    public static CoreRuntime CreateDefault(string? settingsPath = null)
    {
        var registry = new ProviderRegistry(ProviderCatalog.CreateAll());
        var usageFetcher = new UsageFetcher();
        var usageStore = new UsageStore();
        var settingsStore = new SettingsStore(settingsPath);
        var httpApi = new HttpApi();
        var credentialStore = new WindowsCredentialManagerApi();
        var browserCookieApi = new BrowserCookieApi();
        var ptyApi = new ConPtyApi();
        var statusApi = new StatusApi(httpApi);
        var refreshCoordinator = new RefreshCoordinator(
            registry,
            usageFetcher,
            usageStore,
            settingsStore,
            httpApi,
            credentialStore,
            browserCookieApi,
            ptyApi,
            statusApi);
        var codexCostScanner = new CodexCostScanner();

        return new CoreRuntime(
            registry,
            usageFetcher,
            usageStore,
            settingsStore,
            refreshCoordinator,
            codexCostScanner,
            httpApi,
            credentialStore,
            browserCookieApi,
            ptyApi,
            statusApi);
    }
}
