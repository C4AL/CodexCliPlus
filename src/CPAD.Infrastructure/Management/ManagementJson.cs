using System.Globalization;
using System.Text.Json;

namespace CPAD.Infrastructure.Management;

internal static class ManagementJson
{
    public static JsonDocument Parse(string json)
    {
        var payload = string.IsNullOrWhiteSpace(json) ? "{}" : json;
        return JsonDocument.Parse(payload);
    }

    public static string Serialize(object value)
    {
        return JsonSerializer.Serialize(value);
    }

    public static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out value))
            {
                return true;
            }
        }

        foreach (var property in element.EnumerateObject())
        {
            foreach (var name in names)
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    public static JsonElement? GetObject(JsonElement element, params string[] names)
    {
        return TryGetProperty(element, out var value, names) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;
    }

    public static JsonElement? GetArray(JsonElement element, params string[] names)
    {
        return TryGetProperty(element, out var value, names) && value.ValueKind == JsonValueKind.Array
            ? value
            : null;
    }

    public static string? GetString(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return null;
        }

        return AsString(value);
    }

    public static bool? GetBoolean(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return null;
        }

        return AsBoolean(value);
    }

    public static int? GetInt32(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return null;
        }

        return AsInt32(value);
    }

    public static long? GetInt64(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return null;
        }

        return AsInt64(value);
    }

    public static DateTimeOffset? GetDateTimeOffset(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return null;
        }

        return AsDateTimeOffset(value);
    }

    public static IReadOnlyList<string> GetStringList(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            var text = AsString(item);
            if (!string.IsNullOrWhiteSpace(text))
            {
                items.Add(text);
            }
        }

        return items;
    }

    public static IReadOnlyDictionary<string, string> GetStringDictionary(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var items = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            var value = AsString(property.Value);
            if (!string.IsNullOrWhiteSpace(value))
            {
                items[property.Name] = value;
            }
        }

        return items;
    }

    public static IReadOnlyDictionary<string, long> GetLongDictionary(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }

        var items = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            var value = AsInt64(property.Value);
            if (value is not null)
            {
                items[property.Name] = value.Value;
            }
        }

        return items;
    }

    public static string? AsString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    public static bool? AsBoolean(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt64(out var number) => number != 0,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric) => numeric != 0,
            _ => null
        };
    }

    public static int? AsInt32(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        if (value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
        {
            return intValue;
        }

        return null;
    }

    public static long? AsInt64(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var longValue))
        {
            return longValue;
        }

        if (value.ValueKind == JsonValueKind.String &&
            long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out longValue))
        {
            return longValue;
        }

        return null;
    }

    public static DateTimeOffset? AsDateTimeOffset(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        if (AsInt64(value) is { } numeric)
        {
            if (numeric > 0 && numeric < 1_000_000_000_000)
            {
                return DateTimeOffset.FromUnixTimeSeconds(numeric);
            }

            if (numeric >= 1_000_000_000_000)
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(numeric);
            }
        }

        return null;
    }
}
