using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodexBar.Core.Models;
using CodexBar.Core.Utilities;

namespace CodexBar.Core.Providers.Codex;

internal static class CodexParser
{
    private static readonly Regex CreditsRegex = new(@"Credits:\s*\$?(?<amount>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SessionRegex = new(@"(5h|5-hour|session)[^\n]*?(?<pct>\d{1,3})%[^\n]*?(?:resets?\s+in\s+(?<reset>[\w\s]+))?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WeeklyRegex = new(@"(weekly|week)[^\n]*?(?<pct>\d{1,3})%[^\n]*?(?:resets?\s+in\s+(?<reset>[\w\s]+))?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DurationPartRegex = new(@"(?<value>\d+)\s*(?<unit>d|day|days|h|hr|hour|hours|m|min|minute|minutes)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? TryGetAuthToken(string authJsonPath)
    {
        if (!File.Exists(authJsonPath))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(authJsonPath));
            if (JsonLookup.TryGetString(json.RootElement, out var token, "access_token", "accessToken", "id_token", "token") &&
                !string.IsNullOrWhiteSpace(token))
            {
                return token;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    public static ProviderUsageSnapshot? ParseOAuthPayload(JsonElement root, DateTimeOffset nowUtc)
    {
        var sessionPercent = ResolvePercent(root,
            "five_hour_used_percent",
            "session_used_percent",
            "sessionPercent",
            "current_session_percent_used",
            "fiveHourUsagePercent");

        var secondaryPercent = ResolvePercent(root,
            "weekly_used_percent",
            "week_used_percent",
            "weeklyPercent",
            "monthly_used_percent",
            "secondary_used_percent");

        var sessionReset = ResolveReset(root,
            "five_hour_resets_at",
            "session_resets_at",
            "session_reset_at",
            "next_session_reset");

        var secondaryReset = ResolveReset(root,
            "weekly_resets_at",
            "week_resets_at",
            "monthly_resets_at",
            "next_reset_at");

        var credits = ResolveDecimal(root,
            "credits_remaining",
            "credits_remaining_usd",
            "credits",
            "remaining_credits");

        if (sessionPercent is null)
        {
            return null;
        }

        return BuildUsage(sessionPercent.Value, secondaryPercent ?? 0, sessionReset, secondaryReset, credits, nowUtc, note: "source=oauth");
    }

    public static ProviderUsageSnapshot? ParseRpcPayload(JsonElement accountRoot, JsonElement limitsRoot, DateTimeOffset nowUtc)
    {
        var sessionPercent = ResolvePercent(limitsRoot,
            "five_hour_used_percent",
            "session_used_percent",
            "sessionPercent",
            "fiveHourPercentage");

        var secondaryPercent = ResolvePercent(limitsRoot,
            "weekly_used_percent",
            "weeklyPercent",
            "monthly_used_percent");

        var sessionReset = ResolveReset(limitsRoot,
            "five_hour_resets_at",
            "session_resets_at",
            "next_session_reset");

        var secondaryReset = ResolveReset(limitsRoot,
            "weekly_resets_at",
            "monthly_resets_at",
            "next_reset_at");

        var credits = ResolveDecimal(accountRoot,
            "credits",
            "credits_remaining",
            "balance",
            "creditBalance");

        if (sessionPercent is null)
        {
            return null;
        }

        return BuildUsage(sessionPercent.Value, secondaryPercent ?? 0, sessionReset, secondaryReset, credits, nowUtc, note: "source=cli-rpc");
    }

    public static ProviderUsageSnapshot? ParseStatusOutput(string output, DateTimeOffset nowUtc)
    {
        var sessionMatch = SessionRegex.Match(output);
        var weeklyMatch = WeeklyRegex.Match(output);
        if (!sessionMatch.Success)
        {
            return null;
        }

        if (!int.TryParse(sessionMatch.Groups["pct"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sessionPct))
        {
            return null;
        }

        var weeklyPct = 0;
        if (weeklyMatch.Success)
        {
            int.TryParse(weeklyMatch.Groups["pct"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out weeklyPct);
        }

        var sessionIn = ParseDuration(sessionMatch.Groups["reset"].Value);
        var weeklyIn = ParseDuration(weeklyMatch.Groups["reset"].Value);

        decimal? credits = null;
        var creditsMatch = CreditsRegex.Match(output);
        if (creditsMatch.Success &&
            decimal.TryParse(creditsMatch.Groups["amount"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedCredits))
        {
            credits = parsedCredits;
        }

        var sessionReset = sessionIn is null ? null : nowUtc.Add(sessionIn.Value);
        var weeklyReset = weeklyIn is null ? null : nowUtc.Add(weeklyIn.Value);

        return BuildUsage(sessionPct, weeklyPct, sessionReset, weeklyReset, credits, nowUtc, note: "source=cli-pty");
    }

    private static ProviderUsageSnapshot BuildUsage(
        int sessionPercent,
        int secondaryPercent,
        DateTimeOffset? sessionReset,
        DateTimeOffset? secondaryReset,
        decimal? credits,
        DateTimeOffset nowUtc,
        string note)
    {
        return new ProviderUsageSnapshot(
            ProviderId.Codex,
            new UsageWindowSnapshot(
                Label: "5-hour limit",
                UsedPercent: Math.Clamp(sessionPercent, 0, 100),
                ResetAtUtc: sessionReset,
                ResetsIn: sessionReset.HasValue ? sessionReset.Value - nowUtc : null),
            new UsageWindowSnapshot(
                Label: "Weekly limit",
                UsedPercent: Math.Clamp(secondaryPercent, 0, 100),
                ResetAtUtc: secondaryReset,
                ResetsIn: secondaryReset.HasValue ? secondaryReset.Value - nowUtc : null),
            Credits: credits,
            EstimatedTokenCostUsd: null,
            IsStale: false,
            FetchedAtUtc: nowUtc,
            IncidentSummary: null,
            Notes: new[] { note });
    }

    private static int? ResolvePercent(JsonElement root, params string[] keys)
    {
        if (JsonLookup.TryGetInt(root, out var percent, keys))
        {
            return percent;
        }

        if (JsonLookup.TryGetDouble(root, out var used, "used", "current_used") &&
            JsonLookup.TryGetDouble(root, out var limit, "limit", "max", "quota") &&
            limit > 0)
        {
            return (int)Math.Round((used / limit) * 100);
        }

        return null;
    }

    private static DateTimeOffset? ResolveReset(JsonElement root, params string[] keys)
    {
        if (JsonLookup.TryGetDateTimeOffset(root, out var resetAt, keys))
        {
            return resetAt;
        }

        if (JsonLookup.TryGetString(root, out var asText, keys) &&
            DateTimeOffset.TryParse(asText, out var fromText))
        {
            return fromText;
        }

        return null;
    }

    private static decimal? ResolveDecimal(JsonElement root, params string[] keys)
    {
        if (JsonLookup.TryGetDouble(root, out var number, keys))
        {
            return Convert.ToDecimal(number, CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static TimeSpan? ParseDuration(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        double minutes = 0;
        foreach (Match match in DurationPartRegex.Matches(text))
        {
            if (!double.TryParse(match.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            var unit = match.Groups["unit"].Value.ToLowerInvariant();
            if (unit.StartsWith('d'))
            {
                minutes += value * 60 * 24;
            }
            else if (unit.StartsWith('h'))
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
