using CodexBar.Core.Models;

namespace CodexBar.Core.Providers;

public interface IProviderFetchStrategy
{
    string Id { get; }
    FetchKind Kind { get; }

    ValueTask<bool> IsAvailableAsync(ProviderFetchContext context, CancellationToken cancellationToken);
    Task<ProviderFetchResult> FetchAsync(ProviderFetchContext context, CancellationToken cancellationToken);
    bool ShouldFallback(Exception exception, ProviderFetchContext context);
}
