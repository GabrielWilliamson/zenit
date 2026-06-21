using System.Globalization;
using Zenit.Models.CustomReports;

namespace Zenit.Services;

public sealed class ReportFormattingService
{
    public IReadOnlyList<Dictionary<string, object?>> ApplyFormatting(
        IReadOnlyList<Dictionary<string, object?>> rows,
        IReadOnlyList<ReportColumnDefinition> columns,
        IReadOnlyList<ReportFormatDefinition> formattingDefinitions)
    {
        if (rows.Count == 0)
            return rows.ToList();

        var formatMap = BuildFormatMap(columns, formattingDefinitions);
        var output = new List<Dictionary<string, object?>>(rows.Count);

        foreach (var row in rows)
        {
            var formattedRow = new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase);
            foreach (var column in columns.Where(c => c.IsVisible))
            {
                if (!TryResolveValue(formattedRow, column.DisplayName, out var rawValue))
                    continue;

                if (!formatMap.TryGetValue(column.DisplayName, out var format))
                    continue;

                var formatted = FormatValue(rawValue, format, column.DataType);
                formattedRow[column.DisplayName] = formatted;
            }

            output.Add(formattedRow);
        }

        return output;
    }

    private static Dictionary<string, ReportFormatDefinition> BuildFormatMap(
        IReadOnlyList<ReportColumnDefinition> columns,
        IReadOnlyList<ReportFormatDefinition> formattingDefinitions)
    {
        var map = new Dictionary<string, ReportFormatDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns.Where(c => c.IsVisible))
        {
            var format = formattingDefinitions
                .FirstOrDefault(f => string.Equals(f.FieldKey, column.Key, StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(f.FieldKey, column.DisplayName, StringComparison.OrdinalIgnoreCase))
                         ?? InferFromColumn(column);

            map[column.DisplayName] = format;
        }

        return map;
    }

    private static ReportFormatDefinition InferFromColumn(ReportColumnDefinition column)
    {
        var format = new ReportFormatDefinition
        {
            FieldKey = column.Key,
            FormatType = ReportFormatType.Auto,
            DecimalPlaces = column.IsMeasure ? 2 : 0,
            UseThousandsSeparator = true,
            CurrencySymbol = "C$",
            Alignment = column.IsMeasure ? ReportAlignment.Right : ReportAlignment.Left,
            HideZeros = false
        };

        if (string.IsNullOrWhiteSpace(column.FormatString))
            return format;

        var fs = column.FormatString.Trim().ToUpperInvariant();
        if (fs.StartsWith("P", StringComparison.Ordinal))
        {
            format.FormatType = ReportFormatType.Percentage;
            format.DecimalPlaces = ParseDecimalPlaces(fs, 2);
        }
        else if (fs.StartsWith("C", StringComparison.Ordinal))
        {
            format.FormatType = ReportFormatType.Currency;
            format.DecimalPlaces = ParseDecimalPlaces(fs, 2);
        }
        else if (fs.StartsWith("N", StringComparison.Ordinal))
        {
            format.FormatType = ReportFormatType.Decimal;
            format.DecimalPlaces = ParseDecimalPlaces(fs, 2);
        }
        else if (fs.StartsWith("F", StringComparison.Ordinal))
        {
            format.FormatType = ReportFormatType.Decimal;
            format.DecimalPlaces = ParseDecimalPlaces(fs, 2);
            format.UseThousandsSeparator = false;
        }
        else
        {
            format.FormatType = ReportFormatType.Text;
        }

        return format;
    }

    private static int ParseDecimalPlaces(string format, int fallback)
    {
        if (format.Length <= 1)
            return fallback;

        return int.TryParse(format[1..], out var parsed) ? parsed : fallback;
    }

    private static object? FormatValue(object? rawValue, ReportFormatDefinition format, ReportFieldDataType dataType)
    {
        if (rawValue is null)
            return null;

        if (format.FormatType == ReportFormatType.Text)
            return rawValue.ToString();

        if (TryParseDecimal(rawValue, out var number))
        {
            if (format.HideZeros && number == 0m)
                return string.Empty;

            var decimals = Math.Max(0, format.DecimalPlaces);
            var culture = CultureInfo.CurrentCulture;

            return format.FormatType switch
            {
                ReportFormatType.Integer => number.ToString("N0", culture),
                ReportFormatType.Decimal => number.ToString(format.UseThousandsSeparator ? $"N{decimals}" : $"F{decimals}", culture),
                ReportFormatType.Percentage => number.ToString($"P{decimals}", culture),
                ReportFormatType.Currency => FormatCurrency(number, decimals, format.CurrencySymbol, culture),
                ReportFormatType.Accounting => FormatAccounting(number, decimals, format.CurrencySymbol, culture),
                ReportFormatType.Thousands => number.ToString($"N{decimals}", CultureInfo.InvariantCulture),
                ReportFormatType.LocalThousands => number.ToString($"N{decimals}", culture),
                ReportFormatType.Auto => AutoFormat(number, decimals, dataType, culture),
                _ => number.ToString(culture)
            };
        }

        if (TryParseDate(rawValue, out var date))
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return rawValue.ToString();
    }

    private static string AutoFormat(decimal number, int decimals, ReportFieldDataType dataType, CultureInfo culture)
    {
        return dataType switch
        {
            ReportFieldDataType.Integer => number.ToString("N0", culture),
            ReportFieldDataType.Decimal => number.ToString($"N{Math.Max(0, decimals)}", culture),
            _ => number.ToString($"N{Math.Max(0, decimals)}", culture)
        };
    }

    private static string FormatCurrency(decimal number, int decimals, string currencySymbol, CultureInfo culture)
    {
        var formatted = number.ToString($"N{Math.Max(0, decimals)}", culture);
        if (string.IsNullOrWhiteSpace(currencySymbol))
            return formatted;

        return $"{currencySymbol} {formatted}";
    }

    private static string FormatAccounting(decimal number, int decimals, string currencySymbol, CultureInfo culture)
    {
        var absolute = Math.Abs(number).ToString($"N{Math.Max(0, decimals)}", culture);
        var prefix = string.IsNullOrWhiteSpace(currencySymbol) ? string.Empty : $"{currencySymbol} ";

        if (number < 0m)
            return $"({prefix}{absolute})";

        return $"{prefix}{absolute}";
    }

    private static bool TryResolveValue(Dictionary<string, object?> row, string key, out object? value)
    {
        if (row.TryGetValue(key, out value))
            return true;

        var pair = row.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(pair.Key))
        {
            value = null;
            return false;
        }

        value = pair.Value;
        return true;
    }

    private static bool TryParseDecimal(object input, out decimal result)
    {
        if (input is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            result = Convert.ToDecimal(input, CultureInfo.InvariantCulture);
            return true;
        }

        var text = input.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            result = default;
            return false;
        }

        text = text.Trim().Replace("%", string.Empty, StringComparison.Ordinal);
        if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out result))
            return true;

        return decimal.TryParse(text, out result);
    }

    private static bool TryParseDate(object value, out DateTime result)
    {
        if (value is DateTime date)
        {
            result = date;
            return true;
        }

        return DateTime.TryParse(value.ToString(), out result);
    }
}
