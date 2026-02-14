using CodexBar.Core.Models;

namespace CodexBar.Core.Providers;

public sealed class ProviderRegistry
{
    private readonly Dictionary<ProviderId, IProviderDescriptor> _providers;

    public ProviderRegistry(IEnumerable<IProviderDescriptor> providers)
    {
        _providers = providers.ToDictionary(provider => provider.Id);
    }

    public IReadOnlyCollection<IProviderDescriptor> Providers => _providers.Values;

    public IProviderDescriptor GetRequired(ProviderId providerId)
    {
        if (_providers.TryGetValue(providerId, out var provider))
        {
            return provider;
        }

        throw new KeyNotFoundException($"Provider '{providerId}' is not registered.");
    }

    public bool TryGet(ProviderId providerId, out IProviderDescriptor descriptor)
        => _providers.TryGetValue(providerId, out descriptor!);
}
