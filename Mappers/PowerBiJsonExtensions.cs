using System.Globalization;
using System.Text.Json;

namespace Zenit.Mappers;

public static class PowerBiJsonExtensions
{
    public static string GetStringOrDefault(this Dictionary<string, JsonElement> row, string key, string defaultValue = "")
    {
        if (!row.TryGetValue(key, out var value))
            return defaultValue;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? defaultValue,
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => defaultValue
        };
    }

    public static int GetIntOrDefault(this Dictionary<string, JsonElement> row, string key, int defaultValue = 0)
    {
        if (!row.TryGetValue(key, out var value))
            return defaultValue;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;

        var raw = value.ToString();
        return int.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }

    public static decimal GetDecimalOrDefault(this Dictionary<string, JsonElement> row, string key, decimal defaultValue = 0m)
    {
        if (!row.TryGetValue(key, out var value))
            return defaultValue;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            return number;

        var raw = value.ToString();
        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }

    public static DateTime? GetDateTimeOrNull(this Dictionary<string, JsonElement> row, string key)
    {
        if (!row.TryGetValue(key, out var value))
            return null;

        var raw = value.ToString();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        string[] formats =
        {
            "M/d/yyyy",
            "MM/dd/yyyy",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss.fff",
            "yyyy-MM-ddTHH:mm:ssFFFFFFF",
            "yyyy-MM-dd"
        };

        if (DateTime.TryParseExact(
                raw,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var exact))
        {
            return exact;
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var normal))
            return normal;

        return null;
    }
}
