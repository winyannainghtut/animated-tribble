using CodexBar.Core.Models;

namespace CodexBar.Core.Providers;

public sealed class DelegateFetchStrategy : IProviderFetchStrategy
{
    private readonly Func<ProviderFetchContext, CancellationToken, ValueTask<bool>> _availability;
    private readonly Func<ProviderFetchContext, CancellationToken, Task<ProviderFetchResult>> _fetch;
    private readonly Func<Exception, ProviderFetchContext, bool> _shouldFallback;

    public DelegateFetchStrategy(
        string id,
        FetchKind kind,
        Func<ProviderFetchContext, CancellationToken, ValueTask<bool>> availability,
        Func<ProviderFetchContext, CancellationToken, Task<ProviderFetchResult>> fetch,
        Func<Exception, ProviderFetchContext, bool>? shouldFallback = null)
    {
        Id = id;
        Kind = kind;
        _availability = availability;
        _fetch = fetch;
        _shouldFallback = shouldFallback ?? ((_, _) => true);
    }

    public string Id { get; }
    public FetchKind Kind { get; }

    public ValueTask<bool> IsAvailableAsync(ProviderFetchContext context, CancellationToken cancellationToken)
        => _availability(context, cancellationToken);

    public Task<ProviderFetchResult> FetchAsync(ProviderFetchContext context, CancellationToken cancellationToken)
        => _fetch(context, cancellationToken);

    public bool ShouldFallback(Exception exception, ProviderFetchContext context)
        => _shouldFallback(exception, context);
}
