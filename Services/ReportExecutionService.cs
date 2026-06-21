using System.Globalization;
using System.Text;
using Zenit.Infrastructure.PowerBi.Reports;
using Zenit.Infrastructure.Logging;
using Zenit.Models.CustomReports;

namespace Zenit.Services;

public sealed class ReportExecutionService
{
    private readonly PowerBiReportService _reportService;
    private readonly PowerBiDefaultSelectionService _defaultSelectionService;
    private readonly ReportMetadataService _metadataService;
    private readonly ReportColumnService _columnService;
    private readonly ReportSortingService _sortingService;
    private readonly ReportFormattingService _formattingService;
    private readonly ReportRuleEngineService _ruleEngineService;
    private readonly ReportAggregationRuleService _aggregationRuleService;
    private readonly GuidedReportRuleEngineService _guidedRuleEngineService;
    private readonly ILogger<ReportExecutionService> _logger;

    public ReportExecutionService(
        PowerBiReportService reportService,
        PowerBiDefaultSelectionService defaultSelectionService,
        ReportMetadataService metadataService,
        ReportColumnService columnService,
        ReportSortingService sortingService,
        ReportFormattingService formattingService,
        ReportRuleEngineService ruleEngineService,
        ReportAggregationRuleService aggregationRuleService,
        GuidedReportRuleEngineService guidedRuleEngineService,
        ILogger<ReportExecutionService> logger)
    {
        _reportService = reportService;
        _defaultSelectionService = defaultSelectionService;
        _metadataService = metadataService;
        _columnService = columnService;
        _sortingService = sortingService;
        _formattingService = formattingService;
        _ruleEngineService = ruleEngineService;
        _aggregationRuleService = aggregationRuleService;
        _guidedRuleEngineService = guidedRuleEngineService;
        _logger = logger;
    }

    public async Task<ReportExecutionResult> ExecuteAsync(
        ReportExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var datasetId = await ResolveDatasetIdAsync(request.DatasetId, cancellationToken).ConfigureAwait(false);
        var selectedColumns = BuildSelectedColumns(request, Array.Empty<Dictionary<string, object?>>());

        var rawRows = request.SourceMode switch
        {
            ReportSourceMode.FreeCubeQuery => await ExecuteFreeModeAsync(datasetId, request, selectedColumns, cancellationToken).ConfigureAwait(false),
            _ => await ExecuteExistingReportModeAsync(datasetId, request, selectedColumns, cancellationToken).ConfigureAwait(false)
        };

        if (selectedColumns.Count == 0)
            selectedColumns = BuildSelectedColumns(request, rawRows);

        var projectedRows = ProjectRows(rawRows, selectedColumns);
        var sortedRows = _sortingService
            .ApplySorting(projectedRows, request.Sorting, selectedColumns)
            .Select(row => new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var aggregationStyles = _aggregationRuleService.ApplyAggregationRules(sortedRows, request.AggregationRules);
        var ruleStyles = _ruleEngineService.ApplyRules(sortedRows, request.Reglas);
        var guidedRuleResult = _guidedRuleEngineService.ApplyRules(sortedRows, selectedColumns, request.GuidedRules);
        var combinedStyles = aggregationStyles
            .Concat(ruleStyles)
            .Concat(guidedRuleResult.Styles)
            .ToList();

        var finalColumns = BuildFinalColumns(selectedColumns, sortedRows);
        var formattedRows = _formattingService.ApplyFormatting(sortedRows, finalColumns, request.Formatting).ToList();
        var warnings = BuildWarnings(request, formattedRows);
        var summary = BuildSummary(request, formattedRows, finalColumns);
        AppendGuidedRuleSummary(summary, guidedRuleResult.Outcomes);

        return new ReportExecutionResult
        {
            Columns = finalColumns,
            Rows = formattedRows,
            Styles = combinedStyles,
            Summaries = summary,
            Warnings = warnings,
            RuleResults = guidedRuleResult.Outcomes
        };
    }

    private async Task<string> ResolveDatasetIdAsync(string? requestDatasetId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestDatasetId))
            return requestDatasetId.Trim();

        var status = await _defaultSelectionService.EnsureSelectionStateAsync(resolveNames: false, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(status.DatasetId))
            throw new InvalidOperationException("No hay Dataset configurado. Define DefaultDatasetId en Settings.");

        return status.DatasetId;
    }

    private async Task<List<Dictionary<string, object?>>> ExecuteExistingReportModeAsync(
        string datasetId,
        ReportExecutionRequest request,
        IReadOnlyList<ReportColumnDefinition> selectedColumns,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TipoReporte))
            throw new InvalidOperationException("Reporte origen es requerido para el modo basado en reporte existente.");

        if (selectedColumns.Count > 0)
        {
            try
            {
                return await ExecuteFreeModeAsync(datasetId, request, selectedColumns, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo ejecutar query dinamica para reporte existente. Se usa fallback legacy por tipo.");
            }
        }

        var reportType = request.TipoReporte.Trim();
        var year = request.Anio > 2000 ? request.Anio : DateTime.Now.Year;
        var month = request.Mes is >= 1 and <= 12 ? request.Mes : DateTime.Now.Month;

        var groupFilter = BuildGroupFilter(request.FiltrosBase);
        var vendorFilters = BuildVendorFilters(request.Vendedores, request.FiltrosBase);
        var mergedRows = new List<Dictionary<string, object?>>();

        foreach (var vendor in vendorFilters)
        {
            var table = await ExecuteByTypeAsync(
                    reportType,
                    datasetId,
                    year,
                    month,
                    vendor,
                    groupFilter,
                    cancellationToken)
                .ConfigureAwait(false);

            foreach (var row in table.Rows)
                mergedRows.Add(new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase));
        }

        return mergedRows;
    }

    private async Task<List<Dictionary<string, object?>>> ExecuteFreeModeAsync(
        string datasetId,
        ReportExecutionRequest request,
        IReadOnlyList<ReportColumnDefinition> selectedColumns,
        CancellationToken cancellationToken)
    {
        if (selectedColumns.Count == 0)
            throw new InvalidOperationException("Selecciona al menos una columna o medida para modo libre.");

        var dax = BuildFreeModeDax(request, selectedColumns);
        var table = await _reportService
            .ExecuteCustomQueryAsync(datasetId, dax, cancellationToken)
            .ConfigureAwait(false);

        return table.Rows
            .Select(row => new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private async Task<Zenit.Infrastructure.PowerBi.Models.PowerBiQueryTable> ExecuteByTypeAsync(
        string reportType,
        string datasetId,
        int year,
        int month,
        DaxFilterValue? vendor,
        DaxFilterValue? group,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reportType))
            throw new InvalidOperationException("TipoReporte/ReporteOrigen es requerido.");

        var isMayorista = IsMayoristaRoute(vendor);
        var normalized = NormalizeReportType(reportType);

        try
        {
            return await _reportService
                .GetReporteDinamicoAsync(datasetId, reportType, year, month, vendor, group, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallback legacy para reporte '{Reporte}'.", reportType);

            return normalized switch
            {
                "FOCOS" => await _reportService.GetFocosAsync(datasetId, year, month, vendor, group, cancellationToken).ConfigureAwait(false),
                "TA" or "TAKIMBERLY" => await _reportService.GetTaKimberlyAsync(datasetId, year, month, vendor, group, cancellationToken).ConfigureAwait(false),
                "BICCATEGORIAS" => await _reportService.GetBicCategoriasAsync(datasetId, year, month, vendor, group, cancellationToken).ConfigureAwait(false),
                "SEGBAYER" => await _reportService.GetSegBayerAsync(datasetId, year, month, vendor, group, cancellationToken).ConfigureAwait(false),
                "SOLCATEGORIAS" => await _reportService.GetSolCategoriasAsync(datasetId, year, month, vendor, group, cancellationToken).ConfigureAwait(false),
                "PLANINCENTIVOKC" or "PLANINCENTIVOKIMBERLY" => await _reportService.GetPlanIncentivoKimberlyAsync(datasetId, year, month, vendor, group, isMayorista, cancellationToken).ConfigureAwait(false),
                "COBERTURA" => await _reportService.GetTaKimberlyAsync(datasetId, year, month, vendor, group, cancellationToken).ConfigureAwait(false),
                "VENTAS" => await _reportService.GetBicCategoriasAsync(datasetId, year, month, vendor, group, cancellationToken).ConfigureAwait(false),
                "PREMIOS" => await _reportService.GetPlanIncentivoKimberlyAsync(datasetId, year, month, vendor, group, isMayorista, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"No se pudo ejecutar el reporte '{reportType}'. Revisa medidas/filtros del cubo.", ex)
            };
        }
    }

    private static DaxFilterValue? BuildGroupFilter(IReadOnlyList<ReportFilterDefinition> filters)
    {
        var groupValues = filters
            .Where(f => string.Equals(f.Key, "Grupo", StringComparison.OrdinalIgnoreCase))
            .SelectMany(f => f.Values.Any()
                ? (IEnumerable<string>)f.Values
                : new[] { f.Value ?? string.Empty })
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (groupValues.Count == 0)
            return null;

        return DaxFilterValue.FromRaw(groupValues[0]);
    }

    private static List<DaxFilterValue?> BuildVendorFilters(
        IReadOnlyList<string> vendorCodes,
        IReadOnlyList<ReportFilterDefinition> filters)
    {
        var mergedCodes = new List<string>();
        mergedCodes.AddRange(vendorCodes.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()));
        mergedCodes.AddRange(filters
            .Where(f => string.Equals(f.Key, "Vendedores", StringComparison.OrdinalIgnoreCase))
            .SelectMany(f => f.Values.Any()
                ? (IEnumerable<string>)f.Values
                : new[] { f.Value ?? string.Empty })
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim()));

        if (mergedCodes.Count == 0)
            return new List<DaxFilterValue?> { null };

        var filtersList = mergedCodes
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(v => DaxFilterValue.FromRaw(v))
            .Cast<DaxFilterValue?>()
            .ToList();

        if (filtersList.Count == 0)
            filtersList.Add(null);

        return filtersList;
    }

    private static bool IsMayoristaRoute(DaxFilterValue? codVend)
    {
        if (codVend is null)
            return false;

        var normalized = (codVend.Display ?? string.Empty).Trim().TrimStart('0');
        return normalized is "18" or "21" or "31";
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

    private List<ReportColumnDefinition> BuildSelectedColumns(
        ReportExecutionRequest request,
        IReadOnlyList<Dictionary<string, object?>> mergedRows)
    {
        var requestedColumns = request.Columnas
            .Where(c => c.IsVisible)
            .OrderBy(c => c.Order)
            .Select(CloneColumn)
            .ToList();

        if (requestedColumns.Count > 0)
            return requestedColumns;

        if (request.Fields.Count > 0)
        {
            var fromFields = request.Fields
                .Where(field => field.VisibleInColumnSelector)
                .OrderBy(f => f.DefaultOrder)
                .Select((field, index) => new ReportColumnDefinition
                {
                    Key = field.Key,
                    DisplayName = field.DisplayName,
                    SourceTable = field.SourceTable,
                    SourceField = field.SourceField,
                    DataType = field.DataType,
                    SourceType = field.SourceType,
                    IsMeasure = field.IsMeasure,
                    IsDimension = field.IsDimension,
                    IsCalculated = field.IsCalculated,
                    Order = index,
                    IsVisible = true,
                    FormatString = field.DefaultFormat,
                    DefaultFormat = field.DefaultFormat,
                    AllowSorting = field.AllowSorting,
                    AllowFiltering = field.AllowFiltering,
                    AllowRules = field.AllowRules,
                    VisibleInColumnSelector = field.VisibleInColumnSelector,
                    VisibleInAdvancedMode = field.VisibleInAdvancedMode,
                    CatalogCategory = field.UsageCategory.ToString(),
                    CatalogCanonicalKey = field.CanonicalKey
                })
                .ToList();

            if (fromFields.Count > 0)
                return fromFields;
        }

        var inferred = _columnService.BuildColumnsFromRows(request.TipoReporte, mergedRows);
        return inferred
            .Where(c => c.IsVisible)
            .OrderBy(c => c.Order)
            .Select(CloneColumn)
            .ToList();
    }

    private List<Dictionary<string, object?>> ProjectRows(
        IReadOnlyList<Dictionary<string, object?>> rows,
        IReadOnlyList<ReportColumnDefinition> selectedColumns)
    {
        var projected = new List<Dictionary<string, object?>>(rows.Count);
        foreach (var row in rows)
        {
            var output = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in selectedColumns.Where(c => c.IsVisible))
            {
                output[column.DisplayName] = _columnService.ResolveValue(row, column);
            }

            projected.Add(output);
        }

        return projected;
    }

    private static List<ReportColumnDefinition> BuildFinalColumns(
        IReadOnlyList<ReportColumnDefinition> selectedColumns,
        IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var result = selectedColumns
            .Where(c => c.IsVisible)
            .Select(CloneColumn)
            .ToList();

        var used = new HashSet<string>(result.Select(c => c.DisplayName), StringComparer.OrdinalIgnoreCase);
        var nextOrder = result.Count == 0 ? 0 : result.Max(c => c.Order) + 1;

        foreach (var extraKey in rows.SelectMany(r => r.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (used.Contains(extraKey))
                continue;

            result.Add(new ReportColumnDefinition
            {
                Key = extraKey.ToUpperInvariant().Replace(" ", "_", StringComparison.Ordinal),
                DisplayName = extraKey,
                SourceField = extraKey,
                SourceType = InferExtraColumnSource(extraKey),
                DataType = GuessDataType(rows, extraKey),
                IsMeasure = false,
                IsDimension = false,
                IsCalculated = true,
                Order = nextOrder++,
                IsVisible = true,
                AllowSorting = true,
                AllowFiltering = false,
                AllowRules = true,
                VisibleInColumnSelector = false,
                VisibleInAdvancedMode = true,
                CatalogCategory = ReportFieldUsageCategory.Hidden.ToString(),
                CatalogCanonicalKey = extraKey
            });
        }

        return result
            .OrderBy(c => c.Order)
            .ToList();
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

    private static ReportColumnSourceType InferExtraColumnSource(string key)
    {
        return key.Contains("Regla", StringComparison.OrdinalIgnoreCase)
               || key.Contains("Icono", StringComparison.OrdinalIgnoreCase)
               || key.Contains("Check", StringComparison.OrdinalIgnoreCase)
               || key.Contains("Calculad", StringComparison.OrdinalIgnoreCase)
            ? ReportColumnSourceType.RuleOutput
            : ReportColumnSourceType.Calculated;
    }

    private static ReportFieldDataType GuessDataType(
        IReadOnlyList<Dictionary<string, object?>> rows,
        string key)
    {
        var sample = rows
            .Select(r => r.TryGetValue(key, out var value) ? value : null)
            .FirstOrDefault(v => v is not null);

        if (sample is null)
            return ReportFieldDataType.Unknown;

        if (sample is DateTime)
            return ReportFieldDataType.Date;

        if (sample is bool)
            return ReportFieldDataType.Boolean;

        if (sample is byte or sbyte or short or ushort or int or uint or long or ulong)
            return ReportFieldDataType.Integer;

        if (sample is float or double or decimal)
            return ReportFieldDataType.Decimal;

        if (decimal.TryParse(sample.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out _))
            return ReportFieldDataType.Decimal;

        return ReportFieldDataType.Text;
    }

    private static Dictionary<string, object?> BuildSummary(
        ReportExecutionRequest request,
        IReadOnlyList<Dictionary<string, object?>> rows,
        IReadOnlyList<ReportColumnDefinition> columns)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["TemplateId"] = request.TemplateId,
            ["ReporteOrigen"] = request.TipoReporte,
            ["SourceMode"] = request.SourceMode.ToString(),
            ["Mes"] = request.Mes,
            ["Anio"] = request.Anio,
            ["Vendedores"] = request.Vendedores.Count,
            ["Rows"] = rows.Count,
            ["Columns"] = columns.Count,
            ["ExecutedAtUtc"] = DateTime.UtcNow
        };
    }

    private static void AppendGuidedRuleSummary(
        IDictionary<string, object?> summary,
        IReadOnlyList<CustomReportRuleResult> outcomes)
    {
        if (outcomes.Count == 0)
            return;

        summary["GuidedRules"] = outcomes.Count;
        summary["GuidedRulesSucceeded"] = outcomes.Count(result => result.Succeeded);

        var rewards = outcomes
            .Where(result => string.Equals(result.ResultType, CustomReportRuleActionType.Reward.ToString(), StringComparison.OrdinalIgnoreCase)
                             && result.ResultAmount.HasValue)
            .Sum(result => result.ResultAmount!.Value);

        var penalties = outcomes
            .Where(result => string.Equals(result.ResultType, CustomReportRuleActionType.Penalty.ToString(), StringComparison.OrdinalIgnoreCase)
                             && result.ResultAmount.HasValue)
            .Sum(result => result.ResultAmount!.Value);

        if (rewards > 0)
            summary["Rewards"] = rewards;

        if (penalties > 0)
            summary["Penalties"] = penalties;
    }

    private static List<string> BuildWarnings(
        ReportExecutionRequest request,
        IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var warnings = new List<string>();
        if (rows.Count == 0)
            warnings.Add("La consulta no devolvio filas para los filtros actuales.");

        if (request.SourceMode == ReportSourceMode.ExistingReport && string.IsNullOrWhiteSpace(request.TipoReporte))
            warnings.Add("No se especifico un reporte origen para el modo basado en reporte.");

        return warnings;
    }

    private string BuildFreeModeDax(
        ReportExecutionRequest request,
        IReadOnlyList<ReportColumnDefinition> selectedColumns)
    {
        var dimensions = selectedColumns
            .Where(c => c.IsVisible && (c.IsDimension || c.SourceType == ReportColumnSourceType.Dimension))
            .Select(c => c.SourceField)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var measures = selectedColumns
            .Where(c => c.IsVisible && (c.IsMeasure || c.SourceType == ReportColumnSourceType.Measure))
            .ToList();

        if (dimensions.Count == 0 && measures.Count == 0)
            throw new InvalidOperationException("El modo libre requiere al menos una dimension o medida seleccionada.");

        var summarizeArgs = new List<string>();
        summarizeArgs.AddRange(dimensions);
        summarizeArgs.AddRange(BuildDaxFilters(request));

        foreach (var measure in measures)
        {
            var alias = EscapeDaxString(string.IsNullOrWhiteSpace(measure.DisplayName) ? measure.Key : measure.DisplayName);
            var expression = ResolveMeasureExpression(measure);
            summarizeArgs.Add($"\"{alias}\", {expression}");
        }

        var sb = new StringBuilder(2048);
        sb.AppendLine("EVALUATE");
        sb.AppendLine("SUMMARIZECOLUMNS(");
        for (var i = 0; i < summarizeArgs.Count; i++)
        {
            var suffix = i < summarizeArgs.Count - 1 ? "," : string.Empty;
            sb.Append("    ").Append(summarizeArgs[i]).Append(suffix).AppendLine();
        }

        sb.AppendLine(")");

        var orderBy = BuildOrderByForFreeMode(request.Sorting, selectedColumns);
        if (!string.IsNullOrWhiteSpace(orderBy))
        {
            sb.Append("ORDER BY ").AppendLine(orderBy);
        }

        return sb.ToString().Trim();
    }

    private List<string> BuildDaxFilters(ReportExecutionRequest request)
    {
        var filters = new List<string>();
        var filterMap = _metadataService.GetFilterColumnMap();

        if (request.FechaDesde.HasValue || request.FechaHasta.HasValue)
        {
            var from = (request.FechaDesde ?? DateTime.Now.Date).Date;
            var to = (request.FechaHasta ?? request.FechaDesde ?? DateTime.Now.Date).Date;
            if (to < from)
                (from, to) = (to, from);

            filters.Add(BuildDateFilterExpression(from, to));
        }
        else
        {
            var year = request.Anio > 2000 ? request.Anio : DateTime.Now.Year;
            var month = request.Mes is >= 1 and <= 12 ? request.Mes : DateTime.Now.Month;
            var from = new DateTime(year, month, 1);
            var to = from.AddMonths(1).AddDays(-1);
            filters.Add(BuildDateFilterExpression(from, to));
        }

        if (request.SourceMode == ReportSourceMode.ExistingReport && !string.IsNullOrWhiteSpace(request.ReporteOrigen))
        {
            var reportSet = ToDaxSet(new[] { request.ReporteOrigen.Trim() });
            filters.Add($"TREATAS({reportSet}, REPORTES[REPORTE])");
        }

        var normalizedFilters = request.FiltrosBase
            .Where(f => !string.IsNullOrWhiteSpace(f.Key))
            .ToList();

        if (request.Vendedores.Count > 0 && normalizedFilters.All(f => !string.Equals(f.Key, "Vendedores", StringComparison.OrdinalIgnoreCase)))
        {
            normalizedFilters.Add(new ReportFilterDefinition
            {
                Key = "Vendedores",
                Values = request.Vendedores
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            });
        }

        foreach (var filter in normalizedFilters)
        {
            if (!filterMap.TryGetValue(filter.Key, out var columnRef) || string.IsNullOrWhiteSpace(columnRef))
                continue;

            var values = filter.Values.Any()
                ? filter.Values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToList()
                : string.IsNullOrWhiteSpace(filter.Value) ? new List<string>() : new List<string> { filter.Value.Trim() };

            if (values.Count == 0)
                continue;

            filters.Add($"TREATAS({ToDaxSet(values)}, {columnRef})");
        }

        return filters;
    }

    private static string BuildDateFilterExpression(DateTime from, DateTime to)
    {
        return
            "DATESBETWEEN(" +
            "TIEMPO[fecha], " +
            $"DATE({from.Year},{from.Month},{from.Day}), " +
            $"DATE({to.Year},{to.Month},{to.Day}))";
    }

    private static string BuildOrderByForFreeMode(
        IReadOnlyList<ReportSortDefinition> sorting,
        IReadOnlyList<ReportColumnDefinition> selectedColumns)
    {
        if (sorting.Count == 0)
            return string.Empty;

        var parts = new List<string>();
        foreach (var sort in sorting.OrderBy(s => s.Priority))
        {
            var column = selectedColumns.FirstOrDefault(c =>
                string.Equals(c.Key, sort.FieldKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.DisplayName, sort.FieldKey, StringComparison.OrdinalIgnoreCase));

            if (column == null)
                continue;

            var expression = !string.IsNullOrWhiteSpace(column.SourceField)
                ? column.SourceField
                : $"[{EscapeDaxString(column.DisplayName)}]";

            var direction = sort.Direction == ReportSortDirection.Desc ? "DESC" : "ASC";
            parts.Add($"{expression} {direction}");
        }

        return string.Join(", ", parts);
    }

    private static string ResolveMeasureExpression(ReportColumnDefinition measure)
    {
        if (!string.IsNullOrWhiteSpace(measure.SourceField))
        {
            var source = NormalizeLegacyMeasureName(measure.SourceField.Trim());
            if (source.StartsWith("[", StringComparison.Ordinal) && source.EndsWith("]", StringComparison.Ordinal))
                return source;

            if (source.Contains('[', StringComparison.Ordinal) && source.Contains(']', StringComparison.Ordinal))
                return source;

            return $"[{source}]";
        }

        return $"[{measure.Key}]";
    }

    private static string NormalizeLegacyMeasureName(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return source;

        // Compatibilidad: plantillas antiguas guardaron "COB", pero la medida real es "COBERTURA".
        return string.Equals(source, "COB", StringComparison.OrdinalIgnoreCase)
            ? "COBERTURA"
            : source;
    }

    private static string ToDaxSet(IEnumerable<string> values)
    {
        var list = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (list.Count == 0)
            return "{}";

        var literals = list.Select(ToDaxLiteral);
        return "{" + string.Join(",", literals) + "}";
    }

    private static string ToDaxLiteral(string value)
    {
        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var asDecimal))
            return asDecimal.ToString(CultureInfo.InvariantCulture);

        if (decimal.TryParse(value, out asDecimal))
            return asDecimal.ToString(CultureInfo.InvariantCulture);

        return $"\"{EscapeDaxString(value)}\"";
    }

    private static string EscapeDaxString(string value)
    {
        return (value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal);
    }
}
