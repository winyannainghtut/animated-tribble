using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodexBar.Core.Models;
using CodexBar.Core.Utilities;

namespace CodexBar.Core.Providers.Shared;

internal static class GenericProviderParser
{
    private static readonly Regex PercentRegex = new(@"(?<pct>\d{1,3})%", RegexOptions.Compiled);
    private static readonly Regex ResetRegex = new(@"resets?\s+in\s+(?<span>[^\r\n\)]{2,32})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CreditsRegex = new(@"credits?:\s*\$?(?<amount>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SpanPartRegex = new(@"(?<value>\d+)\s*(?<unit>d|day|days|h|hr|hour|hours|m|min|minute|minutes)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ProviderUsageSnapshot? TryParseFromJson(
        ProviderId providerId,
        JsonElement root,
        string sessionLabel,
        string secondaryLabel,
        DateTimeOffset nowUtc,
        string note,
        bool supportsCredits)
    {
        var sessionPercent = ResolvePercent(root, "session_used_percent", "five_hour_used_percent", "used_percent", "sessionPercent");
        var secondaryPercent = ResolvePercent(root, "weekly_used_percent", "monthly_used_percent", "secondary_used_percent", "weeklyPercent");

        if (sessionPercent is null && secondaryPercent is null)
        {
            return null;
        }

        var sessionReset = ResolveReset(root, "session_resets_at", "session_reset_at", "resets_at");
        var secondaryReset = ResolveReset(root, "weekly_resets_at", "monthly_resets_at", "secondary_resets_at", "next_reset_at");

        decimal? credits = null;
        if (supportsCredits && JsonLookup.TryGetDouble(root, out var rawCredits, "credits", "credits_remaining", "creditBalance"))
        {
            credits = Convert.ToDecimal(rawCredits, CultureInfo.InvariantCulture);
        }

        return BuildUsage(
            providerId,
            sessionLabel,
            secondaryLabel,
            sessionPercent ?? 0,
            secondaryPercent ?? 0,
            sessionReset,
            secondaryReset,
            nowUtc,
            credits,
            note);
    }

    public static ProviderUsageSnapshot? TryParseFromText(
        ProviderId providerId,
        string output,
        string sessionLabel,
        string secondaryLabel,
        DateTimeOffset nowUtc,
        string note,
        bool supportsCredits)
    {
        var percents = PercentRegex.Matches(output)
            .Select(match =>
            {
                var parsed = int.TryParse(match.Groups["pct"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent)
                    ? percent
                    : (int?)null;
                return parsed;
            })
            .Where(percent => percent.HasValue)
            .Select(percent => percent!.Value)
            .ToArray();

        if (percents.Length == 0)
        {
            return null;
        }

        var sessionPercent = percents[0];
        var secondaryPercent = percents.Length > 1 ? percents[1] : 0;

        var resetMatches = ResetRegex.Matches(output);
        var sessionIn = resetMatches.Count > 0 ? ParseTimeSpan(resetMatches[0].Groups["span"].Value) : null;
        var secondaryIn = resetMatches.Count > 1 ? ParseTimeSpan(resetMatches[1].Groups["span"].Value) : null;

        decimal? credits = null;
        if (supportsCredits)
        {
            var creditsMatch = CreditsRegex.Match(output);
            if (creditsMatch.Success && decimal.TryParse(creditsMatch.Groups["amount"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                credits = parsed;
            }
        }

        return BuildUsage(
            providerId,
            sessionLabel,
            secondaryLabel,
            sessionPercent,
            secondaryPercent,
            sessionIn.HasValue ? nowUtc.Add(sessionIn.Value) : null,
            secondaryIn.HasValue ? nowUtc.Add(secondaryIn.Value) : null,
            nowUtc,
            credits,
            note);
    }

    public static string BuildCookieHeader(IEnumerable<HostAPIs.BrowserCookie> cookies)
        => string.Join("; ", cookies.Select(cookie => $"{cookie.Name}={cookie.Value}"));

    private static ProviderUsageSnapshot BuildUsage(
        ProviderId providerId,
        string sessionLabel,
        string secondaryLabel,
        int sessionPercent,
        int secondaryPercent,
        DateTimeOffset? sessionReset,
        DateTimeOffset? secondaryReset,
        DateTimeOffset nowUtc,
        decimal? credits,
        string note)
    {
        return new ProviderUsageSnapshot(
            providerId,
            new UsageWindowSnapshot(sessionLabel, Math.Clamp(sessionPercent, 0, 100), sessionReset, sessionReset is null ? null : sessionReset - nowUtc),
            new UsageWindowSnapshot(secondaryLabel, Math.Clamp(secondaryPercent, 0, 100), secondaryReset, secondaryReset is null ? null : secondaryReset - nowUtc),
            credits,
            EstimatedTokenCostUsd: null,
            IsStale: false,
            FetchedAtUtc: nowUtc,
            IncidentSummary: null,
            Notes: new[] { note });
    }

    private static int? ResolvePercent(JsonElement root, params string[] keys)
        => JsonLookup.TryGetInt(root, out var percent, keys) ? percent : null;

    private static DateTimeOffset? ResolveReset(JsonElement root, params string[] keys)
        => JsonLookup.TryGetDateTimeOffset(root, out var reset, keys) ? reset : null;

    private static TimeSpan? ParseTimeSpan(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        double minutes = 0;
        foreach (Match match in SpanPartRegex.Matches(raw))
        {
            if (!double.TryParse(match.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            var unit = match.Groups["unit"].Value;
            if (unit.StartsWith("d", StringComparison.OrdinalIgnoreCase))
            {
                minutes += value * 24 * 60;
            }
            else if (unit.StartsWith("h", StringComparison.OrdinalIgnoreCase))
            {
                minutes += value * 60;
            }
            else
            {
                minutes += value;
            }
        }

        return minutes > 0 ? TimeSpan.FromMinutes(minutes) : null;
    }
}
