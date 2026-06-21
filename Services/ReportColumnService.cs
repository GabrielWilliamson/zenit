using Zenit.Models.CustomReports;

namespace Zenit.Services;

public sealed class ReportColumnService
{
    // Catalogo de compatibilidad para plantillas legacy. El flujo nuevo usa metadata dinamica.
    private static readonly IReadOnlyList<ReportTypeDefinition> ReportTypes = new List<ReportTypeDefinition>
    {
        new() { Key = "PLAN_INCENTIVO_KIMBERLY", DisplayName = "Plan Incentivo Kimberly" },
        new() { Key = "TA_KIMBERLY", DisplayName = "TA Kimberly" },
        new() { Key = "FOCOS", DisplayName = "Focos" },
        new() { Key = "BIC_CATEGORIAS", DisplayName = "BIC Categorias" },
        new() { Key = "SEG_BAYER", DisplayName = "Bayer" },
        new() { Key = "SOL_CATEGORIAS", DisplayName = "Sol Maya Categorias" }
    };

    private static readonly Dictionary<string, List<ReportColumnDefinition>> ColumnCatalog =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Catalogos alineados a los aliases reales del DAX actual.
            ["FOCOS"] = BuildCoberturaCatalog(),
            ["TA_KIMBERLY"] = BuildCoberturaCatalog(),
            ["BIC_CATEGORIAS"] = BuildCorCatalog(),
            ["SOL_CATEGORIAS"] = BuildCorCatalog(),
            ["SEG_BAYER"] = BuildCorYCobCatalog(),
            ["PLAN_INCENTIVO_KIMBERLY"] = BuildPremiosCatalog(),

            // Compatibilidad con plantillas previas (legacy).
            ["COBERTURA"] = BuildCoberturaCatalog(),
            ["VENTAS"] = BuildCorCatalog(),
            ["PREMIOS"] = BuildPremiosCatalog()
        };

    public IReadOnlyList<ReportTypeDefinition> GetReportTypes()
    {
        return ReportTypes
            .Select(t => new ReportTypeDefinition { Key = t.Key, DisplayName = t.DisplayName })
            .ToList();
    }

    public IReadOnlyList<ReportColumnDefinition> GetColumnCatalog(string reportTypeKey)
    {
        if (string.IsNullOrWhiteSpace(reportTypeKey))
            return Array.Empty<ReportColumnDefinition>();

        var resolvedKey = ResolveCatalogKey(reportTypeKey);
        if (!ColumnCatalog.TryGetValue(resolvedKey, out var columns))
            columns = new List<ReportColumnDefinition>();

        return columns
            .Select(Clone)
            .OrderBy(c => c.Order)
            .ToList();
    }

    public IReadOnlyList<ReportColumnDefinition> BuildColumnsFromRows(
        string reportTypeKey,
        IEnumerable<Dictionary<string, object?>> rows,
        IEnumerable<string>? schemaColumns = null)
    {
        var rowsList = rows.ToList();
        var schemaKeys = (schemaColumns ?? Array.Empty<string>())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allKeys = rowsList
            .SelectMany(r => r.Keys)
            .Concat(schemaKeys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Si tenemos filas reales, no pre-cargamos columnas legacy para evitar
        // mezclar campos que no pertenecen al reporte actual.
        var catalog = allKeys.Count == 0
            ? GetColumnCatalog(reportTypeKey).ToList()
            : new List<ReportColumnDefinition>();

        var legacyBySource = GetColumnCatalog(reportTypeKey)
            .GroupBy(column => Normalize(column.SourceField), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => Clone(group.First()), StringComparer.OrdinalIgnoreCase);

        var order = 0;
        foreach (var key in allKeys)
        {
            var sampleValue = rowsList
                .Select(r => r.TryGetValue(key, out var value) ? value : null)
                .FirstOrDefault(v => v is not null);
            var dataType = InferDataType(sampleValue);
            var isMeasure = IsNumeric(sampleValue);
            var sourceType = InferSourceType(key, dataType, isMeasure);

            if (legacyBySource.TryGetValue(Normalize(key), out var legacy))
            {
                legacy.SourceField = key;
                legacy.Order = order++;
                legacy.DataType = dataType == ReportFieldDataType.Unknown ? legacy.DataType : dataType;
                legacy.SourceType = sourceType;
                legacy.IsMeasure = isMeasure || legacy.IsMeasure;
                legacy.IsDimension = sourceType == ReportColumnSourceType.Dimension;
                legacy.IsCalculated = sourceType == ReportColumnSourceType.Calculated;
                catalog.Add(legacy);
                continue;
            }

            catalog.Add(new ReportColumnDefinition
            {
                Key = NormalizeToKey(key),
                DisplayName = SimplifyColumnName(key),
                SourceField = key,
                SourceType = sourceType,
                DataType = dataType,
                IsMeasure = isMeasure,
                IsDimension = sourceType == ReportColumnSourceType.Dimension,
                IsCalculated = sourceType == ReportColumnSourceType.Calculated,
                Order = order++,
                IsVisible = true,
                AllowSorting = true,
                AllowFiltering = sourceType == ReportColumnSourceType.Dimension,
                AllowRules = true,
                VisibleInColumnSelector = true
            });
        }

        if (catalog.Count == 0)
            return GetColumnCatalog(reportTypeKey);

        return catalog
            .OrderBy(c => c.Order)
            .ToList();
    }

    public object? ResolveValue(Dictionary<string, object?> row, ReportColumnDefinition column)
    {
        if (row.TryGetValue(column.SourceField, out var value))
            return value;

        foreach (var alias in GetLegacyAliases(column.SourceField))
        {
            if (row.TryGetValue(alias, out value))
                return value;
        }

        var sourceSimplified = SimplifyColumnName(column.SourceField);
        var bySource = row.FirstOrDefault(kv =>
            string.Equals(SimplifyColumnName(kv.Key), sourceSimplified, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(bySource.Key))
            return bySource.Value;

        var byDisplay = row.FirstOrDefault(kv =>
            string.Equals(SimplifyColumnName(kv.Key), SimplifyColumnName(column.DisplayName), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(byDisplay.Key))
            return byDisplay.Value;

        return null;
    }

    private static IEnumerable<string> GetLegacyAliases(string sourceField)
    {
        var simplified = SimplifyColumnName(sourceField);
        if (string.Equals(simplified, "COB", StringComparison.OrdinalIgnoreCase))
            yield return "COBERTURA";
        else if (string.Equals(simplified, "COBERTURA", StringComparison.OrdinalIgnoreCase))
            yield return "COB";
    }

    private static string Normalize(string value) =>
        SimplifyColumnName(value).Trim().Replace(" ", string.Empty);

    private static string NormalizeToKey(string value)
    {
        var chars = value
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '_')
            .ToArray();
        var key = new string(chars);
        return string.IsNullOrWhiteSpace(key) ? "COL" : key;
    }

    private static ReportColumnDefinition Clone(ReportColumnDefinition column)
    {
        return new ReportColumnDefinition
        {
            Key = column.Key,
            DisplayName = column.DisplayName,
            SourceTable = column.SourceTable,
            SourceField = column.SourceField,
            DataType = column.DataType,
            SourceType = column.SourceType,
            IsMeasure = column.IsMeasure,
            IsDimension = column.IsDimension,
            IsCalculated = column.IsCalculated,
            Order = column.Order,
            IsVisible = column.IsVisible,
            FormatString = column.FormatString,
            DefaultFormat = column.DefaultFormat,
            AllowSorting = column.AllowSorting,
            AllowFiltering = column.AllowFiltering,
            AllowRules = column.AllowRules,
            VisibleInColumnSelector = column.VisibleInColumnSelector,
            VisibleInAdvancedMode = column.VisibleInAdvancedMode,
            CatalogCategory = column.CatalogCategory,
            CatalogCanonicalKey = column.CatalogCanonicalKey
        };
    }

    private static List<ReportColumnDefinition> BuildCoberturaCatalog()
    {
        return BuildColumns(new[]
        {
            ("COD_VEND", "COD_VEND", "COD_VEND", ReportColumnSourceType.Dimension, false, null),
            ("DESCRIPCION", "Descripcion", "DESCRIPCION", ReportColumnSourceType.Dimension, false, null),
            ("MD_COB", "MD_COB", "MD_COB", ReportColumnSourceType.Measure, true, "N2"),
            ("COBERTURA", "COBERTURA", "COBERTURA", ReportColumnSourceType.Measure, true, "N2"),
            ("PORC_MD_COB", "%MD_COB", "%MD_COB", ReportColumnSourceType.Calculated, true, "P2")
        });
    }

    private static List<ReportColumnDefinition> BuildCorCatalog()
    {
        return BuildColumns(new[]
        {
            ("COD_VEND", "COD_VEND", "COD_VEND", ReportColumnSourceType.Dimension, false, null),
            ("DESCRIPCION", "Descripcion", "DESCRIPCION", ReportColumnSourceType.Dimension, false, null),
            ("MD_COR", "MD_COR", "MD_COR", ReportColumnSourceType.Measure, true, "N2"),
            ("CORDOBAS", "CORDOBAS", "CORDOBAS", ReportColumnSourceType.Measure, true, "C2"),
            ("PORC_MD_COR", "%MD_COR", "%MD_COR", ReportColumnSourceType.Calculated, true, "P2")
        });
    }

    private static List<ReportColumnDefinition> BuildCorYCobCatalog()
    {
        return BuildColumns(new[]
        {
            ("COD_VEND", "COD_VEND", "COD_VEND", ReportColumnSourceType.Dimension, false, null),
            ("DESCRIPCION", "Descripcion", "DESCRIPCION", ReportColumnSourceType.Dimension, false, null),
            ("MD_COR", "MD_COR", "MD_COR", ReportColumnSourceType.Measure, true, "N2"),
            ("CORDOBAS", "CORDOBAS", "CORDOBAS", ReportColumnSourceType.Measure, true, "C2"),
            ("PORC_MD_COR", "%MD_COR", "%MD_COR", ReportColumnSourceType.Calculated, true, "P2"),
            ("MD_COB", "MD_COB", "MD_COB", ReportColumnSourceType.Measure, true, "N2"),
            ("COBERTURA", "COBERTURA", "COBERTURA", ReportColumnSourceType.Measure, true, "N2"),
            ("PORC_MD_COB", "%MD_COB", "%MD_COB", ReportColumnSourceType.Calculated, true, "P2")
        });
    }

    private static List<ReportColumnDefinition> BuildPremiosCatalog()
    {
        return BuildColumns(new[]
        {
            ("COD_VEND", "COD_VEND", "COD_VEND", ReportColumnSourceType.Dimension, false, null),
            ("DESCRIPCION", "Descripcion", "DESCRIPCION", ReportColumnSourceType.Dimension, false, null),
            ("MD_COB", "MD_COB", "MD_COB", ReportColumnSourceType.Measure, true, "N2"),
            ("KC", "KC", "KC", ReportColumnSourceType.Measure, true, "N2"),
            ("PORC_MD_KC", "%MD_KC", "%MD_KC", ReportColumnSourceType.Calculated, true, "P2"),
            ("MD_COR", "MD_COR", "MD_COR", ReportColumnSourceType.Measure, true, "N2"),
            ("CORDOBAS", "CORDOBAS", "CORDOBAS", ReportColumnSourceType.Measure, true, "C2"),
            ("PORC_MD_COR", "%MD_COR", "%MD_COR", ReportColumnSourceType.Calculated, true, "P2")
        });
    }

    private static List<ReportColumnDefinition> BuildColumns(
        IReadOnlyList<(string key, string display, string source, ReportColumnSourceType sourceType, bool isMeasure, string? format)> fields)
    {
        var result = new List<ReportColumnDefinition>(fields.Count);
        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            result.Add(new ReportColumnDefinition
            {
                Key = field.key,
                DisplayName = field.display,
                SourceField = field.source,
                SourceType = field.sourceType,
                DataType = field.isMeasure ? ReportFieldDataType.Decimal : ReportFieldDataType.Text,
                IsMeasure = field.isMeasure,
                IsDimension = field.sourceType == ReportColumnSourceType.Dimension,
                IsCalculated = field.sourceType == ReportColumnSourceType.Calculated,
                Order = i,
                IsVisible = true,
                FormatString = field.format,
                DefaultFormat = field.format,
                AllowSorting = true,
                AllowFiltering = field.sourceType == ReportColumnSourceType.Dimension,
                AllowRules = true,
                VisibleInColumnSelector = true
            });
        }

        return result;
    }

    private static string ResolveCatalogKey(string rawReportType)
    {
        var normalized = NormalizeReportType(rawReportType);
        return normalized switch
        {
            "FOCOS" => "FOCOS",
            "TA" or "TAKIMBERLY" => "TA_KIMBERLY",
            "BICCATEGORIAS" => "BIC_CATEGORIAS",
            "SEGBAYER" => "SEG_BAYER",
            "SOLCATEGORIAS" => "SOL_CATEGORIAS",
            "PLANINCENTIVOKC" or "PLANINCENTIVOKIMBERLY" => "PLAN_INCENTIVO_KIMBERLY",
            "COBERTURA" => "COBERTURA",
            "VENTAS" => "VENTAS",
            "PREMIOS" => "PREMIOS",
            _ => rawReportType
        };
    }

    private static string NormalizeReportType(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var chars = value
            .Trim()
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray();

        return new string(chars);
    }

    private static string SimplifyColumnName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var open = name.LastIndexOf('[');
        var close = name.LastIndexOf(']');
        if (open >= 0 && close > open)
            return name.Substring(open + 1, close - open - 1);

        return name;
    }

    private static ReportColumnSourceType InferSourceType(string key, ReportFieldDataType dataType, bool isMeasure)
    {
        if (isMeasure)
            return key.Contains('%', StringComparison.OrdinalIgnoreCase)
                ? ReportColumnSourceType.Calculated
                : ReportColumnSourceType.Measure;

        return IsDimensionKey(key) || dataType is ReportFieldDataType.Text or ReportFieldDataType.Date or ReportFieldDataType.Boolean
            ? ReportColumnSourceType.Dimension
            : ReportColumnSourceType.Calculated;
    }

    private static bool IsDimensionKey(string key)
    {
        var normalized = SimplifyColumnName(key);
        return normalized.Contains("COD", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("DESC", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("CLIENTE", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("NOMBRE", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("CIUDAD", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("DEPARTAMENTO", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("MUNICIPIO", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("DIRECCION", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("ESTADO", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("FECHA", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("ORIGEN", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("CATEGORIA", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("FAMILIA", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("MARCA", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("PROVEEDOR", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("PRODUCTO", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("MOTIVO", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("BODEGA", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("EMPAQUE", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("DIA", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("MES", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("RUTA", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("GRUPO", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("CANAL", StringComparison.OrdinalIgnoreCase)
               || key.Contains('[', StringComparison.Ordinal)
               || key.Contains(']', StringComparison.Ordinal);
    }

    private static bool IsNumeric(object? value)
    {
        return value is sbyte or byte or short or ushort or int or uint or long or ulong
               or float or double or decimal;
    }

    private static ReportFieldDataType InferDataType(object? value)
    {
        if (value is null)
            return ReportFieldDataType.Unknown;

        if (value is bool)
            return ReportFieldDataType.Boolean;

        if (value is DateTime)
            return ReportFieldDataType.Date;

        if (value is sbyte or byte or short or ushort or int or uint or long or ulong)
            return ReportFieldDataType.Integer;

        if (value is float or double or decimal)
            return ReportFieldDataType.Decimal;

        if (decimal.TryParse(value.ToString(), out _))
            return ReportFieldDataType.Decimal;

        return ReportFieldDataType.Text;
    }
}
