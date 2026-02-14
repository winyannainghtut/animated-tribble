using CodexBar.Core.Models;

namespace CodexBar.Core.Providers;

public interface IProviderDescriptor
{
    ProviderId Id { get; }
    string DisplayName { get; }
    string SessionLabel { get; }
    string SecondaryLabel { get; }
    bool SupportsCredits { get; }
    bool SupportsTokenCost { get; }
    bool SupportsStatusPolling { get; }
    bool SupportsLogin { get; }
    string IconResourceName { get; }
    string PrimaryColor { get; }
    string ToggleTitle { get; }
    bool DefaultEnabled { get; }
    IReadOnlyList<IProviderFetchStrategy> FetchStrategies { get; }
}
