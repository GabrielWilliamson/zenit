using Zenit.Infrastructure.PowerBi.Dimensions;
using Zenit.Infrastructure.PowerBi.Reports;
using Zenit.Infrastructure.Configuration;
using Zenit.Infrastructure.Logging;
using Zenit.Models;
using Zenit.Models.CustomReports;

namespace Zenit.Services;

public sealed class ReportMetadataService
{
    private readonly DimensionValuesService _dimensionValuesService;
    private readonly PowerBiDefaultSelectionService _defaultSelectionService;
    private readonly ILogger<ReportMetadataService> _logger;
    private readonly string _reportesColumnRef;
    private readonly string _codVendColumnRef;
    private readonly string _nomVendColumnRef;
    private readonly Dictionary<string, string> _filterColumns;

    public ReportMetadataService(
        DimensionValuesService dimensionValuesService,
        PowerBiDefaultSelectionService defaultSelectionService,
        IConfiguration configuration,
        ILogger<ReportMetadataService> logger)
    {
        _dimensionValuesService = dimensionValuesService;
        _defaultSelectionService = defaultSelectionService;
        _logger = logger;
        _reportesColumnRef = configuration["PowerBi:Dimensions:ReportesColumn"] ?? "REPORTES[REPORTE]";
        _codVendColumnRef = configuration["PowerBi:Dimensions:CodVendColumn"] ?? "VENDEDORES[COD_VEND]";
        _nomVendColumnRef = configuration["PowerBi:Dimensions:NomVenColumn"] ?? "VENDEDORES[NOMVEN]";
        _filterColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Vendedores"] = _codVendColumnRef,
            ["Ruta"] = configuration["PowerBi:Dimensions:RutaColumn"] ?? "VENDEDORES[COD_RUTA]",
            ["Grupo"] = configuration["PowerBi:Dimensions:GrupoColumn"] ?? "VENDEDORES[GRUPO]",
            ["Subgrupo"] = configuration["PowerBi:Dimensions:SubgrupoColumn"] ?? "VENDEDORES[SUBGRUPO]",
            ["Subzona"] = configuration["PowerBi:Dimensions:SubzonaColumn"] ?? "VENDEDORES[SUB_GRUPO2]",
            ["Reporte"] = _reportesColumnRef
        };
    }

    public async Task<string> ResolveDatasetIdAsync(
        string? datasetId = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(datasetId))
            return datasetId.Trim();

        var selection = await _defaultSelectionService
            .EnsureSelectionStateAsync(resolveNames: false, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(selection.DatasetId))
            throw new InvalidOperationException("No hay Dataset configurado. Define DefaultDatasetId en Settings.");

        return selection.DatasetId;
    }

    public async Task<IReadOnlyList<ReportSourceDefinition>> GetReportSourcesAsync(
        string? datasetId = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedDatasetId = await ResolveDatasetIdAsync(datasetId, cancellationToken).ConfigureAwait(false);
        var rawValues = await _dimensionValuesService
            .GetDistinctValuesAsync(resolvedDatasetId, _reportesColumnRef, cancellationToken)
            .ConfigureAwait(false);

        return rawValues
            .Select(v => v.Display?.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
            .Select(v => new ReportSourceDefinition
            {
                ReporteNombre = v!,
                DisplayName = v!,
                IsActive = true
            })
            .ToList();
    }

    public async Task<IReadOnlyList<FilterOption>> GetVendorOptionsAsync(
        string? datasetId = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedDatasetId = await ResolveDatasetIdAsync(datasetId, cancellationToken).ConfigureAwait(false);
        var vendors = await _dimensionValuesService
            .GetDistinctValuesAsync(resolvedDatasetId, _codVendColumnRef, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyDictionary<string, string> vendorNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            vendorNameMap = await _dimensionValuesService
                .GetVendorNameMapAsync(resolvedDatasetId, _codVendColumnRef, _nomVendColumnRef, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo cargar NOMVEN para vendedores. Se mostrara solo COD_VEND.");
        }

        return vendors
            .Select(v =>
            {
                var code = v.Display?.Trim() ?? string.Empty;
                var display = vendorNameMap.TryGetValue(code, out var nomven) && !string.IsNullOrWhiteSpace(nomven)
                    ? $"{code} - {nomven}"
                    : code;

                return new FilterOption
                {
                    DisplayName = display,
                    Value = v
                };
            })
            .Where(v => !string.IsNullOrWhiteSpace(v.DisplayName))
            .OrderBy(v => v.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyDictionary<string, string> GetFilterColumnMap()
        => new Dictionary<string, string>(_filterColumns, StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<FilterOption>> GetFilterOptionsAsync(
        string filterKey,
        string? datasetId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filterKey))
            return Array.Empty<FilterOption>();

        if (string.Equals(filterKey, "Vendedores", StringComparison.OrdinalIgnoreCase))
            return await GetVendorOptionsAsync(datasetId, cancellationToken).ConfigureAwait(false);

        if (string.Equals(filterKey, "Reporte", StringComparison.OrdinalIgnoreCase))
        {
            var reports = await GetReportSourcesAsync(datasetId, cancellationToken).ConfigureAwait(false);
            return reports
                .Select(r => new FilterOption
                {
                    DisplayName = r.DisplayName,
                    Value = DaxFilterValue.FromRaw(r.ReporteNombre)
                })
                .OrderBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        if (!_filterColumns.TryGetValue(filterKey, out var columnRef) || string.IsNullOrWhiteSpace(columnRef))
            return Array.Empty<FilterOption>();

        var resolvedDatasetId = await ResolveDatasetIdAsync(datasetId, cancellationToken).ConfigureAwait(false);
        var values = await _dimensionValuesService
            .GetDistinctValuesAsync(resolvedDatasetId, columnRef, cancellationToken)
            .ConfigureAwait(false);

        return values
            .Select(v => new FilterOption
            {
                DisplayName = v.Display,
                Value = v
            })
            .Where(v => !string.IsNullOrWhiteSpace(v.DisplayName))
            .OrderBy(v => v.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
