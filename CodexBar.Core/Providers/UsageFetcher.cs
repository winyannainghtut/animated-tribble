using CodexBar.Core.Models;

namespace CodexBar.Core.Providers;

public sealed class UsageFetcher
{
    public async Task<ProviderFetchResult> FetchAsync(
        IProviderDescriptor provider,
        ProviderFetchContext context,
        CancellationToken cancellationToken)
    {
        var attempts = new List<FetchAttempt>();

        foreach (var strategy in provider.FetchStrategies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool available;
            try
            {
                available = await strategy.IsAvailableAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                attempts.Add(new FetchAttempt(
                    strategy.Id,
                    strategy.Kind,
                    Available: false,
                    Succeeded: false,
                    Message: $"availability check failed: {ex.Message}",
                    TimestampUtc: context.NowUtc));
                continue;
            }

            if (!available)
            {
                attempts.Add(new FetchAttempt(
                    strategy.Id,
                    strategy.Kind,
                    Available: false,
                    Succeeded: false,
                    Message: "not available",
                    TimestampUtc: context.NowUtc));
                continue;
            }

            try
            {
                var strategyResult = await strategy.FetchAsync(context, cancellationToken).ConfigureAwait(false);
                attempts.Add(new FetchAttempt(
                    strategy.Id,
                    strategy.Kind,
                    Available: true,
                    Succeeded: strategyResult.Success,
                    Message: strategyResult.Success ? "success" : strategyResult.Error ?? "failed",
                    TimestampUtc: context.NowUtc));

                if (strategyResult.Success)
                {
                    return new ProviderFetchResult(
                        provider.Id,
                        strategyResult.Usage,
                        Success: true,
                        IsStale: strategyResult.IsStale,
                        Error: null,
                        Attempts: attempts);
                }

                if (!strategy.ShouldFallback(new InvalidOperationException(strategyResult.Error ?? "fetch failed"), context))
                {
                    return new ProviderFetchResult(
                        provider.Id,
                        strategyResult.Usage,
                        Success: false,
                        IsStale: true,
                        Error: strategyResult.Error,
                        Attempts: attempts);
                }
            }
            catch (Exception ex)
            {
                attempts.Add(new FetchAttempt(
                    strategy.Id,
                    strategy.Kind,
                    Available: true,
                    Succeeded: false,
                    Message: ex.Message,
                    TimestampUtc: context.NowUtc));

                if (!strategy.ShouldFallback(ex, context))
                {
                    return ProviderFetchResult.Failed(provider.Id, ex.Message, attempts);
                }
            }
        }

        return ProviderFetchResult.Failed(provider.Id, "No available strategy succeeded.", attempts);
    }
}
