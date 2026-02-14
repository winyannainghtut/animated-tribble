namespace CodexBar.Core.Utilities;

public static class TimeFormatting
{
    public static string FormatRelative(TimeSpan? time)
    {
        if (!time.HasValue)
        {
            return "N/A";
        }

        var t = time.Value;
        if (t.TotalDays >= 1)
        {
            return $"{(int)t.TotalDays}d {t.Hours}h";
        }

        if (t.TotalHours >= 1)
        {
            return $"{(int)t.TotalHours}h {t.Minutes}m";
        }

        return $"{Math.Max(0, (int)t.TotalMinutes)}m";
    }
}
