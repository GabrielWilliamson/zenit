using System.Collections;
using System.Data;
using System.Globalization;
using Zenit.Models.CustomReports;

namespace Zenit.Helpers;

public static class TabularPreviewFormatter
{
    public static string FromDataTable(DataTable? table, int maxRows = 50)
    {
        if (table == null || table.Columns.Count == 0)
            return "Todavia no hay resultados.";

        var headers = table.Columns.Cast<DataColumn>().Select(column => column.ColumnName).ToList();
        var rows = table.Rows.Cast<DataRow>()
            .Take(maxRows)
            .Select(row => headers.ToDictionary(
                header => header,
                header => row[header] == DBNull.Value ? null : row[header]));

        return FromRows(rows, headers, maxRows, table.Rows.Count);
    }

    public static string FromExecutionRows(
        IEnumerable? rows,
        IReadOnlyList<ReportColumnDefinition> columns,
        int maxRows = 50)
    {
        var dictionaries = rows?.Cast<object?>()
            .OfType<Dictionary<string, object?>>()
            .ToList();

        if (dictionaries == null || dictionaries.Count == 0)
            return "Todavia no hay resultados.";

        var headers = columns
            .Where(column => column.IsVisible)
            .OrderBy(column => column.Order)
            .Select(column => column.DisplayName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        if (headers.Count == 0)
        {
            headers = dictionaries
                .SelectMany(row => row.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return FromRows(dictionaries.Take(maxRows), headers, maxRows, dictionaries.Count);
    }

    private static string FromRows(
        IEnumerable<IDictionary<string, object?>> rows,
        IReadOnlyList<string> headers,
        int maxRows,
        int totalRows)
    {
        if (headers.Count == 0)
            return "Todavia no hay resultados.";

        var lines = new List<string>
        {
            string.Join(" | ", headers)
        };

        foreach (var row in rows)
        {
            lines.Add(string.Join(" | ", headers.Select(header => FormatValue(ResolveValue(row, header)))));
        }

        if (totalRows > maxRows)
            lines.Add($"... mostrando {maxRows} de {totalRows} filas");

        return string.Join(Environment.NewLine, lines);
    }

    private static object? ResolveValue(IDictionary<string, object?> row, string key)
    {
        if (row.TryGetValue(key, out var value))
            return value;

        var match = row.FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(match.Key) ? null : match.Value;
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            DateTime dateTime => dateTime.ToString("g", CultureInfo.CurrentCulture),
            decimal number => number.ToString("0.##", CultureInfo.CurrentCulture),
            double number => number.ToString("0.##", CultureInfo.CurrentCulture),
            float number => number.ToString("0.##", CultureInfo.CurrentCulture),
            _ => value.ToString() ?? string.Empty
        };
    }
}
