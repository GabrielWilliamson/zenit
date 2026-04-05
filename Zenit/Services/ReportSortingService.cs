using Zenit.Models.CustomReports;

namespace Zenit.Services;

public sealed class ReportSortingService
{
    public IReadOnlyList<Dictionary<string, object?>> ApplySorting(
        IReadOnlyList<Dictionary<string, object?>> rows,
        IReadOnlyList<ReportSortDefinition> sorting,
        IReadOnlyList<ReportColumnDefinition> columns)
    {
        if (rows.Count == 0)
            return rows.ToList();

        var effectiveSorting = BuildEffectiveSorting(sorting, columns);
        if (effectiveSorting.Count == 0)
            return rows.ToList();

        IOrderedEnumerable<Dictionary<string, object?>>? ordered = null;

        foreach (var sort in effectiveSorting.OrderBy(s => s.Priority))
        {
            var comparer = new DynamicValueComparer();

            if (ordered == null)
            {
                ordered = sort.Direction == ReportSortDirection.Asc
                    ? rows.OrderBy(row => GetValueForSort(row, sort), comparer)
                    : rows.OrderByDescending(row => GetValueForSort(row, sort), comparer);
            }
            else
            {
                ordered = sort.Direction == ReportSortDirection.Asc
                    ? ordered.ThenBy(row => GetValueForSort(row, sort), comparer)
                    : ordered.ThenByDescending(row => GetValueForSort(row, sort), comparer);
            }
        }

        return (ordered ?? rows.OrderBy(_ => 0)).ToList();
    }

    private static IReadOnlyList<ReportSortDefinition> BuildEffectiveSorting(
        IReadOnlyList<ReportSortDefinition> sorting,
        IReadOnlyList<ReportColumnDefinition> columns)
    {
        if (sorting.Count > 0)
        {
            return sorting
                .Where(s => !string.IsNullOrWhiteSpace(s.FieldKey))
                .OrderBy(s => s.Priority)
                .ToList();
        }

        // Orden por defecto: seguir el primer campo visible para estabilidad.
        var firstColumn = columns
            .OrderBy(c => c.Order)
            .FirstOrDefault(c => c.IsVisible);

        if (firstColumn == null)
            return Array.Empty<ReportSortDefinition>();

        return new[]
        {
            new ReportSortDefinition
            {
                FieldKey = firstColumn.DisplayName,
                DisplayName = firstColumn.DisplayName,
                Direction = ReportSortDirection.Asc,
                Priority = 0
            }
        };
    }

    private static object? GetValueForSort(
        Dictionary<string, object?> row,
        ReportSortDefinition sortDefinition)
    {
        var key = sortDefinition.DisplayName;
        if (string.IsNullOrWhiteSpace(key))
            key = sortDefinition.FieldKey;

        if (string.IsNullOrWhiteSpace(key))
            return null;

        if (row.TryGetValue(key, out var exact))
            return exact;

        var match = row.FirstOrDefault(kv =>
            string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Normalize(kv.Key), Normalize(key), StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(match.Key) ? null : match.Value;
    }

    private static string Normalize(string value)
    {
        return (value ?? string.Empty)
            .Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private sealed class DynamicValueComparer : IComparer<object?>
    {
        public int Compare(object? x, object? y)
        {
            return CompareValues(x, y);
        }

        private static int CompareValues(object? x, object? y)
        {
            if (ReferenceEquals(x, y))
                return 0;

            if (x is null)
                return 1;

            if (y is null)
                return -1;

            if (TryToDecimal(x, out var xd) && TryToDecimal(y, out var yd))
                return xd.CompareTo(yd);

            if (TryToDateTime(x, out var xt) && TryToDateTime(y, out var yt))
                return xt.CompareTo(yt);

            var sx = x.ToString() ?? string.Empty;
            var sy = y.ToString() ?? string.Empty;
            return string.Compare(sx, sy, StringComparison.CurrentCultureIgnoreCase);
        }

        private static bool TryToDecimal(object value, out decimal result)
        {
            if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
            {
                result = Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }

            var text = value.ToString();
            if (string.IsNullOrWhiteSpace(text))
            {
                result = default;
                return false;
            }

            text = text.Trim().Replace("%", string.Empty, StringComparison.Ordinal);
            if (decimal.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out result))
                return true;

            return decimal.TryParse(text, out result);
        }

        private static bool TryToDateTime(object value, out DateTime result)
        {
            if (value is DateTime dt)
            {
                result = dt;
                return true;
            }

            return DateTime.TryParse(value.ToString(), out result);
        }
    }
}
