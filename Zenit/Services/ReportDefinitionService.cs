using Zenit.Core.Infrastructure.PowerBi.Reports;
using Zenit.Infrastructure.Logging;
using Zenit.Models.CustomReports;

namespace Zenit.Services;

public sealed class ReportDefinitionService
{
    private readonly PowerBiReportService _reportService;
    private readonly ReportColumnService _columnService;
    private readonly ILogger<ReportDefinitionService> _logger;

    public ReportDefinitionService(
        PowerBiReportService reportService,
        ReportColumnService columnService,
        ILogger<ReportDefinitionService> logger)
    {
        _reportService = reportService;
        _columnService = columnService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ReportColumnDefinition>> GetColumnDefinitionsAsync(
        string datasetId,
        string reporteOrigen,
        int anio,
        int mes,
        IReadOnlyList<string>? vendedores = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(datasetId))
            throw new ArgumentException("datasetId es requerido");

        if (string.IsNullOrWhiteSpace(reporteOrigen))
            return Array.Empty<ReportColumnDefinition>();

        var validYear = anio > 2000 ? anio : DateTime.Now.Year;
        var validMonth = mes is >= 1 and <= 12 ? mes : DateTime.Now.Month;

        var sampleRows = new List<Dictionary<string, object?>>();
        var sampledColumnNames = new List<string>();
        foreach (var vendorFilter in BuildProbeVendorFilters(vendedores))
        {
            try
            {
                var table = await _reportService
                    .GetReporteDinamicoAsync(datasetId, reporteOrigen, validYear, validMonth, vendorFilter, null, cancellationToken)
                    .ConfigureAwait(false);

                sampledColumnNames.AddRange(table.Columns
                    .Select(column => column.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name)));

                foreach (var row in table.Rows)
                {
                    sampleRows.Add(new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase));
                    if (sampleRows.Count >= 200)
                        break;
                }

                if (sampleRows.Count >= 200)
                    break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo inferir columnas para reporte '{ReporteOrigen}'.", reporteOrigen);
            }
        }

        var catalog = _columnService
            .BuildColumnsFromRows(reporteOrigen, sampleRows, sampledColumnNames)
            .OrderBy(c => c.Order)
            .Select(CloneColumn)
            .ToList();

        for (var i = 0; i < catalog.Count; i++)
        {
            var column = catalog[i];
            EnrichColumnMetadata(column, sampleRows);
            column.Order = i;
        }

        return catalog;
    }

    private static IReadOnlyList<DaxFilterValue?> BuildProbeVendorFilters(IReadOnlyList<string>? vendorCodes)
    {
        if (vendorCodes == null || vendorCodes.Count == 0)
            return new DaxFilterValue?[] { null };

        var filters = vendorCodes
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => DaxFilterValue.FromRaw(v.Trim()))
            .DistinctBy(v => v.Display, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .Cast<DaxFilterValue?>()
            .ToList();

        if (filters.Count == 0)
            filters.Add(null);

        return filters;
    }

    private void EnrichColumnMetadata(ReportColumnDefinition column, IReadOnlyList<Dictionary<string, object?>> sampleRows)
    {
        if (string.IsNullOrWhiteSpace(column.Key))
            column.Key = NormalizeToKey(column.SourceField);

        if (string.IsNullOrWhiteSpace(column.DisplayName))
            column.DisplayName = SimplifyColumnName(column.SourceField);

        var sample = sampleRows
            .Select(row => _columnService.ResolveValue(row, column))
            .FirstOrDefault(value => value is not null && !string.IsNullOrWhiteSpace(value.ToString()));

        if (column.DataType == ReportFieldDataType.Unknown)
            column.DataType = InferDataType(sample);

        if (column.SourceType == ReportColumnSourceType.Unknown)
        {
            if (IsDimensionLike(column.SourceField)
                || column.DataType is ReportFieldDataType.Text or ReportFieldDataType.Date or ReportFieldDataType.Boolean)
            {
                column.SourceType = ReportColumnSourceType.Dimension;
                column.IsMeasure = false;
            }
            else if (IsNumeric(sample) || column.DataType is ReportFieldDataType.Integer or ReportFieldDataType.Decimal)
            {
                column.SourceType = ReportColumnSourceType.Measure;
                column.IsMeasure = true;
            }
            else
            {
                column.SourceType = ReportColumnSourceType.Calculated;
            }
        }

        if (!column.IsMeasure && column.SourceType == ReportColumnSourceType.Measure)
            column.IsMeasure = true;
        if (!column.IsDimension && column.SourceType == ReportColumnSourceType.Dimension)
            column.IsDimension = true;
        if (!column.IsCalculated && column.SourceType == ReportColumnSourceType.Calculated)
            column.IsCalculated = true;

        if (string.IsNullOrWhiteSpace(column.FormatString) && column.IsMeasure)
        {
            var simplified = SimplifyColumnName(column.SourceField);
            if (simplified.Contains('%', StringComparison.OrdinalIgnoreCase) ||
                simplified.Contains("PORC", StringComparison.OrdinalIgnoreCase))
            {
                column.FormatString = "P2";
            }
            else if (simplified.Contains("CORDOBA", StringComparison.OrdinalIgnoreCase) ||
                     simplified.Contains("PREMIO", StringComparison.OrdinalIgnoreCase) ||
                     simplified.Contains("AFECTACION", StringComparison.OrdinalIgnoreCase))
            {
                column.FormatString = "C2";
            }
            else
            {
                column.FormatString = "N2";
            }
        }
    }

    private static bool IsDimensionLike(string sourceField)
    {
        var simplified = SimplifyColumnName(sourceField);
        return simplified.Contains("COD", StringComparison.OrdinalIgnoreCase)
               || simplified.Contains("DESC", StringComparison.OrdinalIgnoreCase)
               || simplified.Contains("CLIENTE", StringComparison.OrdinalIgnoreCase)
               || simplified.Contains("NOMBRE", StringComparison.OrdinalIgnoreCase)
               || simplified.Contains("CIUDAD", StringComparison.OrdinalIgnoreCase)
               || simplified.Contains("DEPARTAMENTO", StringComparison.OrdinalIgnoreCase)
               || simplified.Contains("MUNICIPIO", StringComparison.OrdinalIgnoreCase)
               || simplified.Contains("DIRECCION", StringComparison.OrdinalIgnoreCase)
               || simplified.Contains("ESTADO", StringComparison.OrdinalIgnoreCase)
               || simplified.Contains("FECHA", StringComparison.OrdinalIgnoreCase)
               || simplified.Contains("ORIGEN", StringComparison.OrdinalIgnoreCase)
               || simplified.Contains("CATEGORIA", StringComparison.OrdinalIgnoreCase)
               || simplified.Contains("FAMILIA", StringComparison.OrdinalIgnoreCase)
               || simplified.Contains("MARCA", StringComparison.OrdinalIgnoreCase)
               || simplified.Contains("PROVEEDOR", StringComparison.OrdinalIgnoreCase)
               || simplified.Contains("PRODUCTO", StringComparison.OrdinalIgnoreCase)
               || simplified.Contains("MOTIVO", StringComparison.OrdinalIgnoreCase)
               || simplified.Contains("BODEGA", StringComparison.OrdinalIgnoreCase)
               || simplified.Contains("EMPAQUE", StringComparison.OrdinalIgnoreCase)
               || simplified.Contains("DIA", StringComparison.OrdinalIgnoreCase)
               || simplified.Contains("MES", StringComparison.OrdinalIgnoreCase)
               || simplified.Contains("GRUPO", StringComparison.OrdinalIgnoreCase)
               || simplified.Contains("RUTA", StringComparison.OrdinalIgnoreCase)
               || simplified.Contains("CANAL", StringComparison.OrdinalIgnoreCase)
               || sourceField.Contains('[', StringComparison.Ordinal)
               || sourceField.Contains(']', StringComparison.Ordinal);
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

        if (value is DateTime)
            return ReportFieldDataType.Date;

        if (value is bool)
            return ReportFieldDataType.Boolean;

        if (value is sbyte or byte or short or ushort or int or uint or long or ulong)
            return ReportFieldDataType.Integer;

        if (value is float or double or decimal)
            return ReportFieldDataType.Decimal;

        if (decimal.TryParse(value.ToString(), out _))
            return ReportFieldDataType.Decimal;

        return ReportFieldDataType.Text;
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

    private static string NormalizeToKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "COL";

        var chars = value
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '_')
            .ToArray();
        var key = new string(chars);
        return string.IsNullOrWhiteSpace(key) ? "COL" : key;
    }

    private static ReportColumnDefinition CloneColumn(ReportColumnDefinition column)
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
}
