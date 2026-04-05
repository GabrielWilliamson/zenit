using Zenit.Models;
using Zenit.Models.CustomReports;
using System.Linq;
using Zenit.Infrastructure.Logging;

namespace Zenit.Services;

public sealed class DynamicReportService
{
    private readonly ReportMetadataService _metadataService;
    private readonly ReportDefinitionService _definitionService;
    private readonly ReportFieldCatalogService _fieldCatalogService;
    private readonly ReportTemplateService _templateService;
    private readonly ReportExecutionService _executionService;
    private readonly ReportTemplateRuleSchemaService _ruleSchemaService;
    private readonly ILogger<DynamicReportService> _logger;

    public DynamicReportService(
        ReportMetadataService metadataService,
        ReportDefinitionService definitionService,
        ReportFieldCatalogService fieldCatalogService,
        ReportTemplateService templateService,
        ReportExecutionService executionService,
        ReportTemplateRuleSchemaService ruleSchemaService,
        ILogger<DynamicReportService> logger)
    {
        _metadataService = metadataService;
        _definitionService = definitionService;
        _fieldCatalogService = fieldCatalogService;
        _templateService = templateService;
        _executionService = executionService;
        _ruleSchemaService = ruleSchemaService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ReportTypeDefinition>> GetReportTypesAsync(
        string? datasetId = null,
        CancellationToken cancellationToken = default)
    {
        var sources = await _metadataService
            .GetReportSourcesAsync(datasetId, cancellationToken)
            .ConfigureAwait(false);

        return sources
            .Select(s => new ReportTypeDefinition
            {
                Key = s.ReporteNombre,
                DisplayName = s.DisplayName
            })
            .ToList();
    }

    public Task<IReadOnlyList<FilterOption>> GetVendorOptionsAsync(
        string? datasetId = null,
        CancellationToken cancellationToken = default)
        => _metadataService.GetVendorOptionsAsync(datasetId, cancellationToken);

    public Task<IReadOnlyList<FilterOption>> GetFilterOptionsAsync(
        string filterKey,
        string? datasetId = null,
        CancellationToken cancellationToken = default)
        => _metadataService.GetFilterOptionsAsync(filterKey, datasetId, cancellationToken);

    public Task<IReadOnlyList<ReportFieldDefinition>> GetFieldCatalogAsync(
        string? datasetId,
        ReportSourceMode sourceMode,
        string? reporteOrigen,
        int anio,
        int mes,
        IReadOnlyList<string>? vendedores = null,
        CancellationToken cancellationToken = default)
        => _fieldCatalogService.GetFieldCatalogAsync(datasetId, sourceMode, reporteOrigen, anio, mes, vendedores, cancellationToken);

    public async Task<IReadOnlyList<ReportColumnDefinition>> GetColumnCatalogAsync(
        string? datasetId,
        string reporteOrigen,
        int anio,
        int mes,
        IReadOnlyList<string>? vendedores = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedDatasetId = await _metadataService
            .ResolveDatasetIdAsync(datasetId, cancellationToken)
            .ConfigureAwait(false);

        var inferredColumns = await _definitionService
            .GetColumnDefinitionsAsync(
                resolvedDatasetId,
                reporteOrigen,
                anio,
                mes,
                vendedores,
                cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<ReportFieldDefinition> fieldCatalog = Array.Empty<ReportFieldDefinition>();
        try
        {
            fieldCatalog = await _fieldCatalogService
                .GetFieldCatalogAsync(
                    resolvedDatasetId,
                    ReportSourceMode.ExistingReport,
                    reporteOrigen,
                    anio,
                    mes,
                    vendedores,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo cargar catalogo de campos BI para complementar columnas.");
        }

        var merged = new List<ReportColumnDefinition>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in inferredColumns.OrderBy(column => column.Order))
        {
            if (IsArtificialUiColumn(column))
                continue;

            var cloned = CloneColumn(column);
            ApplyCatalogFlags(cloned, fieldCatalog);
            var identity = BuildFieldIdentity(cloned.SourceField, cloned.Key, cloned.DisplayName);
            if (seen.Add(identity))
                merged.Add(cloned);
        }

        foreach (var field in fieldCatalog
                     .Where(field => !IsArtificialUiField(field))
                     .Where(field => field.VisibleInColumnSelector || field.VisibleInAdvancedMode)
                     .OrderBy(field => GetSourceOrder(field.SourceType))
                     .ThenBy(field => field.DefaultOrder)
                     .ThenBy(field => field.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var identity = BuildFieldIdentity(field.SourceField, field.Key, field.DisplayName);
            if (!seen.Add(identity))
                continue;

            merged.Add(ToColumn(field, merged.Count));
        }

        for (var i = 0; i < merged.Count; i++)
            merged[i].Order = i;

        return merged;
    }

    public Task<IReadOnlyList<ReportTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default)
        => _templateService.GetAllAsync(cancellationToken);

    public Task<ReportTemplate> SaveTemplateAsync(ReportTemplate template, CancellationToken cancellationToken = default)
        => _templateService.SaveAsync(template, cancellationToken);

    public Task<ReportTemplate> DuplicateTemplateAsync(Guid templateId, string? newName = null, CancellationToken cancellationToken = default)
        => _templateService.DuplicateAsync(templateId, newName, cancellationToken);

    public Task DeleteTemplateAsync(Guid templateId, CancellationToken cancellationToken = default)
        => _templateService.DeleteAsync(templateId, cancellationToken);

    public async Task<ReportExecutionResult> ExecuteAsync(
        ReportExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.TipoReporte) && !string.IsNullOrWhiteSpace(request.ReporteOrigen))
            request.TipoReporte = request.ReporteOrigen;

        if (request.SourceMode == ReportSourceMode.ExistingReport && string.IsNullOrWhiteSpace(request.TipoReporte))
            throw new InvalidOperationException("Reporte origen es requerido para ejecutar.");

        if (request.Anio <= 0)
            request.Anio = DateTime.Now.Year;

        if (request.Mes is < 1 or > 12)
            request.Mes = DateTime.Now.Month;

        var result = await _executionService.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.Summaries.Count == 0)
        {
            result.Summaries["Rows"] = result.Rows.Count;
            result.Summaries["Columns"] = result.Columns.Count;
            result.Summaries["ExecutedAtUtc"] = DateTime.UtcNow;
        }

        _logger.LogInformation(
            "Reporte dinamico ejecutado. Modo: {Mode}. Reporte: {Reporte}. Filas: {Rows}. Columnas: {Columns}.",
            request.SourceMode,
            request.TipoReporte,
            result.Rows.Count,
            result.Columns.Count);

        return result;
    }

    public async Task<ReportExecutionResult> ExecuteTemplateAsync(
        ReportTemplate template,
        string? datasetId,
        int anio,
        int mes,
        IReadOnlyList<string>? vendedores = null,
        IReadOnlyList<ReportFilterDefinition>? filtrosBase = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);

        var source = string.IsNullOrWhiteSpace(template.ReporteOrigen) ? template.TipoReporte : template.ReporteOrigen;
        if (string.IsNullOrWhiteSpace(source))
            throw new InvalidOperationException("La plantilla no tiene source configurado.");

        var fieldCatalog = await _fieldCatalogService
            .GetFieldCatalogAsync(
                datasetId,
                ReportSourceMode.ExistingReport,
                source,
                anio,
                mes,
                vendedores,
                cancellationToken)
            .ConfigureAwait(false);

        var columns = BuildTemplateColumns(template, fieldCatalog);
        var parseResult = _ruleSchemaService.Parse(template.Ruth, fieldCatalog);

        var request = new ReportExecutionRequest
        {
            TemplateId = template.Id,
            Nombre = template.Nombre,
            SourceMode = ReportSourceMode.ExistingReport,
            DatasetId = datasetId,
            TipoReporte = source,
            ReporteOrigen = source,
            Anio = anio,
            Mes = mes,
            Vendedores = vendedores?.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>(),
            FiltrosBase = filtrosBase?.Select(CloneFilter).ToList() ?? new List<ReportFilterDefinition>(),
            Columnas = columns,
            Fields = fieldCatalog.ToList(),
            GuidedRules = parseResult.GuidedRules,
            Reglas = template.Reglas.ToList(),
            AggregationRules = template.AggregationRules.ToList()
        };

        var result = await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);

        if (parseResult.PreservedLegacyRules.Count > 0)
        {
            result.Warnings.Add(
                $"{parseResult.PreservedLegacyRules.Count} regla(s) legacy se conservaron en la plantilla, pero esta pantalla ejecuta solo las reglas guiadas compatibles.");
        }

        return result;
    }

    private static List<ReportColumnDefinition> BuildTemplateColumns(
        ReportTemplate template,
        IReadOnlyList<ReportFieldDefinition> fieldCatalog)
    {
        var requestedKeys = ParseColumnDesign(template.ColumnDesign);
        var catalog = fieldCatalog.ToList();
        var result = new List<ReportColumnDefinition>();

        if (requestedKeys.Count == 0 && template.Columnas.Count > 0)
        {
            requestedKeys = template.Columnas
                .OrderBy(column => column.Order)
                .Select(column => string.IsNullOrWhiteSpace(column.SourceField) ? column.Key : column.SourceField)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
        }

        foreach (var key in requestedKeys)
        {
            var match = ResolveField(catalog, key);
            if (match != null)
            {
                result.Add(ToColumn(match, result.Count));
                continue;
            }

            var fallback = template.Columnas.FirstOrDefault(column =>
                IsSameKey(column.SourceField, key)
                || IsSameKey(column.Key, key)
                || IsSameKey(column.DisplayName, key));

            if (fallback != null)
            {
                var cloned = CloneColumn(fallback);
                cloned.Order = result.Count;
                cloned.IsVisible = true;
                result.Add(cloned);
                continue;
            }

            result.Add(new ReportColumnDefinition
            {
                Key = key,
                DisplayName = key,
                SourceField = key,
                Order = result.Count,
                IsVisible = true,
                VisibleInColumnSelector = true
            });
        }

        if (result.Count > 0)
            return result;

        return catalog
            .OrderBy(field => field.DefaultOrder)
            .Take(Math.Min(8, catalog.Count))
            .Select((field, index) => ToColumn(field, index))
            .ToList();
    }

    private static ReportFieldDefinition? ResolveField(
        IEnumerable<ReportFieldDefinition> fieldCatalog,
        string key)
    {
        return fieldCatalog.FirstOrDefault(field =>
                   IsSameKey(field.SourceField, key)
                   || IsSameKey(field.Key, key)
                   || IsSameKey(field.DisplayName, key));
    }

    private static ReportColumnDefinition ToColumn(ReportFieldDefinition field, int order)
        => new()
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
            Order = order,
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
        };

    private static ReportFilterDefinition CloneFilter(ReportFilterDefinition filter)
        => new()
        {
            Key = filter.Key,
            DisplayName = filter.DisplayName,
            Value = filter.Value,
            Values = filter.Values.ToList(),
            IsRequired = filter.IsRequired,
            AllowMultiple = filter.AllowMultiple
        };

    private static int GetSourceOrder(ReportColumnSourceType sourceType)
    {
        return sourceType switch
        {
            ReportColumnSourceType.Dimension => 0,
            ReportColumnSourceType.Measure => 1,
            ReportColumnSourceType.Calculated => 2,
            ReportColumnSourceType.RuleOutput => 3,
            _ => 4
        };
    }

    private static string BuildFieldIdentity(string? sourceField, string? key, string? displayName)
    {
        var raw = !string.IsNullOrWhiteSpace(sourceField)
            ? sourceField
            : !string.IsNullOrWhiteSpace(key)
                ? key
                : displayName ?? string.Empty;

        return NormalizeKey(raw);
    }

    private static bool IsArtificialUiColumn(ReportColumnDefinition column)
    {
        if (!column.IsCalculated
            && column.SourceType is not ReportColumnSourceType.Calculated
            and not ReportColumnSourceType.RuleOutput
            and not ReportColumnSourceType.Unknown)
        {
            return false;
        }

        return IsArtificialUiName(column.DisplayName)
               || IsArtificialUiName(column.SourceField)
               || IsArtificialUiName(column.Key);
    }

    private static bool IsArtificialUiField(ReportFieldDefinition field)
    {
        if (!field.IsCalculated
            && field.SourceType is not ReportColumnSourceType.Calculated
            and not ReportColumnSourceType.RuleOutput
            and not ReportColumnSourceType.Unknown)
        {
            return false;
        }

        return IsArtificialUiName(field.DisplayName)
               || IsArtificialUiName(field.SourceField)
               || IsArtificialUiName(field.Key);
    }

    private static bool IsArtificialUiName(string? value)
    {
        var normalized = NormalizeKey(value);
        return normalized is "SEMAFORO"
            or "CHECK"
            or "ESTADO"
            or "STATUS"
            or "OBSERVACION"
            or "PREMIOCALCULADO"
            or "AFECTACIONCALCULADA"
            or "REGLASGUIADASAPLICADAS";
    }

    private static ReportColumnDefinition CloneColumn(ReportColumnDefinition column)
        => new()
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

    private static void ApplyCatalogFlags(
        ReportColumnDefinition column,
        IReadOnlyList<ReportFieldDefinition> fieldCatalog)
    {
        var match = fieldCatalog.FirstOrDefault(field =>
            IsSameKey(field.SourceField, column.SourceField)
            || IsSameKey(field.Key, column.SourceField)
            || IsSameKey(field.DisplayName, column.DisplayName)
            || IsSameKey(field.Key, column.Key));

        if (match == null)
            return;

        column.VisibleInColumnSelector = match.VisibleInColumnSelector;
        column.VisibleInAdvancedMode = match.VisibleInAdvancedMode;
        column.CatalogCategory = match.UsageCategory.ToString();
        column.CatalogCanonicalKey = match.CanonicalKey;
    }

    private static List<string> ParseColumnDesign(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new List<string>();

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();
    }

    private static bool IsSameKey(string? left, string? right)
        => string.Equals(NormalizeKey(left), NormalizeKey(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var source = value.Trim();
        var open = source.LastIndexOf('[');
        var close = source.LastIndexOf(']');
        if (open >= 0 && close > open)
            source = source[(open + 1)..close];

        source = source.Replace("%", "PCT", StringComparison.Ordinal);

        return new string(source
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '+')
            .Select(char.ToUpperInvariant)
            .ToArray());
    }
}
