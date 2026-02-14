using CodexBar.Core.Models;
using CodexBar.Core.Providers;
using CodexBar.Core.Services;

namespace CodexBar.Cli;

internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        if (!TryParseArgs(args, out var parsed, out var parseError))
        {
            Console.WriteLine($"Error: {parseError}");
            PrintUsage();
            return 1;
        }

        if (parsed.ShowHelp)
        {
            PrintUsage();
            return 0;
        }

        var runtime = CoreRuntimeFactory.CreateDefault();
        var providerId = parsed.ProviderId ?? ProviderId.Codex;

        var provider = runtime.ProviderRegistry.GetRequired(providerId);
        var result = await FetchUsageAsync(runtime, provider, parsed.Verbose);

        if (result.Error != null)
        {
            Console.WriteLine($"Error: {result.Error}");
            return 1;
        }

        var usage = result.Usage;
        if (usage is null)
        {
            Console.WriteLine("Error: Usage unavailable.");
            return 1;
        }

        Console.WriteLine($"{provider.DisplayName} Usage:");
        Console.WriteLine($"  {provider.SessionLabel}: {usage.Session.UsedPercent}% (resets in {FormatTime(usage.Session.ResetsIn)})");
        Console.WriteLine($"  {provider.SecondaryLabel}: {usage.Secondary.UsedPercent}% (resets in {FormatTime(usage.Secondary.ResetsIn)})");

        if (usage.Credits.HasValue)
        {
            Console.WriteLine($"  Credits: ${usage.Credits.Value:F2}");
        }

        if (parsed.Verbose)
        {
            Console.WriteLine($"  Stale: {usage.IsStale}");
            Console.WriteLine($"  Fetched: {usage.FetchedAtUtc:O}");

            if (!string.IsNullOrWhiteSpace(usage.IncidentSummary))
            {
                Console.WriteLine($"  Incident: {usage.IncidentSummary}");
            }

            if (usage.Notes.Count > 0)
            {
                Console.WriteLine("  Notes:");
                foreach (var note in usage.Notes)
                {
                    Console.WriteLine($"    - {note}");
                }
            }

            if (result.Attempts.Count > 0)
            {
                Console.WriteLine("  Attempts:");
                foreach (var attempt in result.Attempts)
                {
                    Console.WriteLine($"    - [{attempt.Kind}] {attempt.StrategyId}: {attempt.Message}");
                }
            }
        }

        return 0;
    }

    private static async Task<ProviderFetchResult> FetchUsageAsync(CoreRuntime runtime, IProviderDescriptor provider, bool verbose)
    {
        var refreshed = await runtime.RefreshCoordinator
            .RefreshAsync(new[] { provider.Id }, respectSettings: false, verbose: verbose)
            .ConfigureAwait(false);

        if (refreshed.Count > 0)
        {
            return refreshed[0];
        }

        return ProviderFetchResult.Failed(provider.Id, "Provider refresh did not return a result.");
    }

    private static bool TryParseArgs(string[] args, out CliArgs parsed, out string? error)
    {
        parsed = new CliArgs();
        error = null;

        if (args.Length == 0)
        {
            return true;
        }

        if (args[0] is "--help" or "-h")
        {
            parsed = parsed with { ShowHelp = true };
            return true;
        }

        if (!string.Equals(args[0], "usage", StringComparison.OrdinalIgnoreCase))
        {
            error = $"Unknown command '{args[0]}'.";
            return false;
        }

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-p":
                case "--provider":
                    if (i + 1 >= args.Length)
                    {
                        error = "Missing provider value after -p/--provider.";
                        return false;
                    }

                    var rawProvider = args[++i];
                    if (!TryParseProvider(rawProvider, out var providerId))
                    {
                        error = $"Unknown provider '{rawProvider}'.";
                        return false;
                    }

                    parsed = parsed with { ProviderId = providerId };
                    break;

                case "--verbose":
                    parsed = parsed with { Verbose = true };
                    break;

                case "--help":
                case "-h":
                    parsed = parsed with { ShowHelp = true };
                    break;

                default:
                    error = $"Unknown argument '{arg}'.";
                    return false;
            }
        }

        return true;
    }

    private static bool TryParseProvider(string rawProvider, out ProviderId providerId)
    {
        foreach (var candidate in Enum.GetValues<ProviderId>())
        {
            var enumName = candidate.ToString();
            var display = candidate.ToDisplayName();
            var normalizedDisplay = display.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
            var normalizedRaw = rawProvider.Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);

            if (string.Equals(enumName, rawProvider, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(display, rawProvider, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedDisplay, normalizedRaw, StringComparison.OrdinalIgnoreCase))
            {
                providerId = candidate;
                return true;
            }
        }

        providerId = default;
        return false;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  codexbar usage [-p|--provider <provider>] [--verbose]");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  codexbar usage");
        Console.WriteLine("  codexbar usage -p codex");
        Console.WriteLine("  codexbar usage -p claude --verbose");
    }

    private static string FormatTime(TimeSpan? time)
    {
        if (!time.HasValue)
        {
            return "N/A";
        }

        var t = time.Value;
        if (t.TotalHours >= 24)
        {
            return $"{(int)t.TotalDays}d {(int)t.Hours % 24}h";
        }

        if (t.TotalHours >= 1)
        {
            return $"{(int)t.TotalHours}h {(int)t.Minutes % 60}m";
        }

        return $"{(int)t.TotalMinutes}m";
    }

    private sealed record CliArgs(
        ProviderId? ProviderId = null,
        bool Verbose = false,
        bool ShowHelp = false);
}
