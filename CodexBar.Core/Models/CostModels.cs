namespace CodexBar.Core.Models;

public sealed record CostSample(
    DateTimeOffset TimestampUtc,
    string Model,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd,
    string SourceFile
);

public sealed record ProviderCostSummary(
    ProviderId ProviderId,
    int Days,
    decimal TotalCostUsd,
    int InputTokens,
    int OutputTokens,
    IReadOnlyDictionary<string, decimal> CostByModelUsd,
    int FilesScanned,
    int RecordsMatched
);
