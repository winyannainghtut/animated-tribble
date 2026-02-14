using CodexBar.Core.Models;

namespace CodexBar.Core.Providers;

public sealed class ProviderDescriptor : IProviderDescriptor
{
    public required ProviderId Id { get; init; }
    public required string DisplayName { get; init; }
    public required string SessionLabel { get; init; }
    public required string SecondaryLabel { get; init; }
    public required bool SupportsCredits { get; init; }
    public required bool SupportsTokenCost { get; init; }
    public required bool SupportsStatusPolling { get; init; }
    public required bool SupportsLogin { get; init; }
    public required string IconResourceName { get; init; }
    public required string PrimaryColor { get; init; }
    public required string ToggleTitle { get; init; }
    public required bool DefaultEnabled { get; init; }
    public required IReadOnlyList<IProviderFetchStrategy> FetchStrategies { get; init; }
}
