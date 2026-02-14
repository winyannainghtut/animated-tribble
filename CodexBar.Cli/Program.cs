using CodexBar.Core.Models;
using CodexBar.Core.Providers;

namespace CodexBar.Cli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var provider = new CodexProvider();
        var context = new ProviderFetchContext(
            HomePath: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ConfigPath: Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codex"
            )
        );

        var result = await provider.FetchAsync(context);

        if (result.Error != null)
        {
            Console.WriteLine($"Error: {result.Error}");
            return 1;
        }

        var usage = result.Usage;
        Console.WriteLine($"{provider.DisplayName} Usage:");
        Console.WriteLine($"  {provider.SessionLabel}: {usage.SessionUsedPercent}% (resets in {FormatTime(usage.ResetsIn)})");
        Console.WriteLine($"  {provider.WeeklyLabel}: {usage.WeeklyUsedPercent}% (resets in {FormatTime(usage.ResetsIn)})");

        if (usage.Credits.HasValue)
        {
            Console.WriteLine($"  Credits: ${usage.Credits.Value:F2}");
        }

        return 0;
    }

    static string FormatTime(TimeSpan? time)
    {
        if (!time.HasValue) return "N/A";
        var t = time.Value;
        if (t.TotalHours >= 24) return $"{(int)t.TotalDays}d {(int)t.Hours % 24}h";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {(int)t.Minutes % 60}m";
        return $"{(int)t.TotalMinutes}m";
    }
}
