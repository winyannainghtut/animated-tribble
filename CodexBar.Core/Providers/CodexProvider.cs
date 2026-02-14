using CodexBar.Core.Models;
using CodexBar.Core.Providers.Codex;

namespace CodexBar.Core.Providers;

// Backward-compatible alias for older references.
public sealed class CodexProvider : IProviderDescriptor
{
    private readonly CodexDescriptor _inner = new();

    public ProviderId Id => _inner.Id;
    public string DisplayName => _inner.DisplayName;
    public string SessionLabel => _inner.SessionLabel;
    public string SecondaryLabel => _inner.SecondaryLabel;
    public bool SupportsCredits => _inner.SupportsCredits;
    public bool SupportsTokenCost => _inner.SupportsTokenCost;
    public bool SupportsStatusPolling => _inner.SupportsStatusPolling;
    public bool SupportsLogin => _inner.SupportsLogin;
    public string IconResourceName => _inner.IconResourceName;
    public string PrimaryColor => _inner.PrimaryColor;
    public string ToggleTitle => _inner.ToggleTitle;
    public bool DefaultEnabled => _inner.DefaultEnabled;
    public IReadOnlyList<IProviderFetchStrategy> FetchStrategies => _inner.FetchStrategies;
}
