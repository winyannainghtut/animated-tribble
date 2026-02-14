using System.Text.Json;
using CodexBar.Core.Models;
using CodexBar.Core.Utilities;

namespace CodexBar.Core.Services;

public sealed class CodexCostScanner
{
    private static readonly Dictionary<string, (decimal InputPer1M, decimal OutputPer1M)> ModelPricing =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt-5"] = (1.25m, 10.00m),
            ["gpt-5-mini"] = (0.25m, 2.00m),
            ["gpt-5-nano"] = (0.05m, 0.40m),
            ["gpt-4.1"] = (2.00m, 8.00m),
            ["gpt-4o"] = (5.00m, 15.00m),
            ["o4-mini"] = (1.10m, 4.40m),
            ["o3"] = (2.00m, 8.00m)
        };

    public async Task<ProviderCostSummary> ScanAsync(string homeDirectory, int days, CancellationToken cancellationToken)
    {
        var sessionsRoot = Path.Combine(homeDirectory, ".codex", "sessions");
        if (!Directory.Exists(sessionsRoot))
        {
            return new ProviderCostSummary(
                ProviderId.Codex,
                days,
                TotalCostUsd: 0,
                InputTokens: 0,
                OutputTokens: 0,
                CostByModelUsd: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase),
                FilesScanned: 0,
                RecordsMatched: 0);
        }

        var threshold = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, days));
        var jsonlFiles = Directory
            .EnumerateFiles(sessionsRoot, "*.jsonl", SearchOption.AllDirectories)
            .ToArray();

        var byModel = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        decimal totalCost = 0;
        var totalInputTokens = 0;
        var totalOutputTokens = 0;
        var matched = 0;

        foreach (var jsonlFile in jsonlFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var stream = File.OpenRead(jsonlFile);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    using var json = JsonDocument.Parse(line);
                    var root = json.RootElement;

                    if (!TryResolveTimestamp(root, out var timestampUtc) || timestampUtc < threshold)
                    {
                        continue;
                    }

                    if (!TryResolveModel(root, out var model))
                    {
                        continue;
                    }

                    var inputTokens = ResolveTokenCount(root, "input_tokens", "prompt_tokens", "inputTokens");
                    var outputTokens = ResolveTokenCount(root, "output_tokens", "completion_tokens", "outputTokens");
                    if (inputTokens == 0 && outputTokens == 0)
                    {
                        continue;
                    }

                    var price = ResolvePricing(model);
                    var cost = ((inputTokens / 1_000_000m) * price.InputPer1M) + ((outputTokens / 1_000_000m) * price.OutputPer1M);

                    totalCost += cost;
                    totalInputTokens += inputTokens;
                    totalOutputTokens += outputTokens;
                    matched++;

                    if (!byModel.TryGetValue(model, out var current))
                    {
                        current = 0;
                    }

                    byModel[model] = current + cost;
                }
                catch
                {
                    // Ignore malformed line.
                }
            }
        }

        return new ProviderCostSummary(
            ProviderId.Codex,
            days,
            decimal.Round(totalCost, 6),
            totalInputTokens,
            totalOutputTokens,
            byModel,
            FilesScanned: jsonlFiles.Length,
            RecordsMatched: matched);
    }

    private static bool TryResolveTimestamp(JsonElement root, out DateTimeOffset timestamp)
    {
        if (JsonLookup.TryGetDateTimeOffset(root, out timestamp, "timestamp", "created_at", "createdAt"))
        {
            return true;
        }

        if (JsonLookup.TryGetDouble(root, out var unixSeconds, "timestamp_unix", "ts"))
        {
            timestamp = DateTimeOffset.FromUnixTimeSeconds((long)unixSeconds);
            return true;
        }

        timestamp = default;
        return false;
    }

    private static bool TryResolveModel(JsonElement root, out string model)
    {
        if (JsonLookup.TryGetString(root, out var found, "model", "model_name", "modelName") && !string.IsNullOrWhiteSpace(found))
        {
            model = found;
            return true;
        }

        model = string.Empty;
        return false;
    }

    private static int ResolveTokenCount(JsonElement root, params string[] keys)
        => JsonLookup.TryGetInt(root, out var count, keys) ? Math.Max(0, count) : 0;

    private static (decimal InputPer1M, decimal OutputPer1M) ResolvePricing(string model)
    {
        foreach (var kvp in ModelPricing)
        {
            if (model.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }

        return (1.25m, 10.00m);
    }
}
