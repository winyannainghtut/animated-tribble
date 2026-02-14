using System.Text.Json;

namespace CodexBar.Core.Utilities;

public static class JsonLookup
{
    public static bool TryGetDouble(JsonElement root, out double value, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (TryFindByKey(root, key, out var element) && TryConvertToDouble(element, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    public static bool TryGetInt(JsonElement root, out int value, params string[] keys)
    {
        if (TryGetDouble(root, out var d, keys))
        {
            value = (int)Math.Round(d);
            return true;
        }

        value = default;
        return false;
    }

    public static bool TryGetString(JsonElement root, out string? value, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryFindByKey(root, key, out var element))
            {
                continue;
            }

            value = element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }

    public static bool TryGetDateTimeOffset(JsonElement root, out DateTimeOffset value, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryFindByKey(root, key, out var element))
            {
                continue;
            }

            if (element.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(element.GetString(), out value))
            {
                return true;
            }

            if (element.ValueKind == JsonValueKind.Number)
            {
                if (element.TryGetInt64(out var unixMs))
                {
                    try
                    {
                        value = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
                        return true;
                    }
                    catch
                    {
                        // Ignore invalid numbers.
                    }
                }
            }
        }

        value = default;
        return false;
    }

    private static bool TryFindByKey(JsonElement element, string key, out JsonElement found)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(key) || property.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    found = property.Value;
                    return true;
                }

                if (TryFindByKey(property.Value, key, out found))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindByKey(item, key, out found))
                {
                    return true;
                }
            }
        }

        found = default;
        return false;
    }

    private static bool TryConvertToDouble(JsonElement element, out double value)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetDouble(out value);
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString();
            return double.TryParse(text, out value);
        }

        value = default;
        return false;
    }
}
