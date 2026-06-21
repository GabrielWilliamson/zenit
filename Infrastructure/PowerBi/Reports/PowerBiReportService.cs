using System;
using System.Threading;
using System.Threading.Tasks;
using Zenit.Infrastructure.PowerBi.Models;
using Zenit.Infrastructure.PowerBi.Queries;

namespace Zenit.Infrastructure.PowerBi.Reports;

/// <summary>
/// Servicio "alto nivel" para ejecutar reportes.
/// Recibe filtros ya tipados (DaxFilterValue) para evitar errores de tipos y respetar ceros.
/// </summary>
public sealed class PowerBiReportService
{
    private readonly ExecuteQueryService _executeQueryService;
    private readonly ExecuteQueriesResponseParser _parser;

    // Columnas configurables (por si tu modelo usa otro nombre).
    private readonly string _codVendColumn;
    private readonly string _grupoColumn;

    public PowerBiReportService(
        ExecuteQueryService executeQueryService,
        ExecuteQueriesResponseParser parser,
        string codVendColumn,
        string grupoColumn)
    {
        _executeQueryService = executeQueryService;
        _parser = parser;

        _codVendColumn = string.IsNullOrWhiteSpace(codVendColumn) ? "VENDEDORES[COD_VEND]" : codVendColumn;
        _grupoColumn = string.IsNullOrWhiteSpace(grupoColumn) ? "VENDEDORES[GRUPO]" : grupoColumn;
    }

    public async Task<PowerBiQueryTable> GetPlanIncentivoKimberlyAsync(
        string datasetId,
        int year,
        int month,
        DaxFilterValue? codVend = null,
        DaxFilterValue? grupo = null,
        bool mayoristas = false,
        CancellationToken cancellationToken = default)
    {
        var dax = mayoristas
            ? DaxReportFactory.BuildPlanIncentivoMayoristas(year, month, codVend, grupo, _codVendColumn, _grupoColumn)
            : DaxReportFactory.BuildPlanIncentivoKc(year, month, codVend, grupo, _codVendColumn, _grupoColumn);

        var json = await _executeQueryService.ExecuteAsync(datasetId, dax, cancellationToken).ConfigureAwait(false);
        return _parser.ParseFirstTable(json);
    }

    public async Task<PowerBiQueryTable> GetTaKimberlyAsync(
        string datasetId,
        int year,
        int month,
        DaxFilterValue? codVend = null,
        DaxFilterValue? grupo = null,
        CancellationToken cancellationToken = default)
    {
        var dax = DaxReportFactory.BuildTa(year, month, codVend, grupo, _codVendColumn, _grupoColumn);
        var json = await _executeQueryService.ExecuteAsync(datasetId, dax, cancellationToken).ConfigureAwait(false);
        return _parser.ParseFirstTable(json);
    }

    public async Task<PowerBiQueryTable> GetFocosAsync(
        string datasetId,
        int year,
        int month,
        DaxFilterValue? codVend = null,
        DaxFilterValue? grupo = null,
        CancellationToken cancellationToken = default)
    {
        var dax = DaxReportFactory.BuildFocos(year, month, codVend, grupo, _codVendColumn, _grupoColumn);
        var json = await _executeQueryService.ExecuteAsync(datasetId, dax, cancellationToken).ConfigureAwait(false);
        return _parser.ParseFirstTable(json);
    }

    public async Task<PowerBiQueryTable> GetBicCategoriasAsync(
        string datasetId,
        int year,
        int month,
        DaxFilterValue? codVend = null,
        DaxFilterValue? grupo = null,
        CancellationToken cancellationToken = default)
    {
        var dax = DaxReportFactory.BuildBicCategorias(year, month, codVend, grupo, _codVendColumn, _grupoColumn);
        var json = await _executeQueryService.ExecuteAsync(datasetId, dax, cancellationToken).ConfigureAwait(false);
        return _parser.ParseFirstTable(json);
    }

    public async Task<PowerBiQueryTable> GetSegBayerAsync(
        string datasetId,
        int year,
        int month,
        DaxFilterValue? codVend = null,
        DaxFilterValue? grupo = null,
        CancellationToken cancellationToken = default)
    {
        var dax = DaxReportFactory.BuildSegBayer(year, month, codVend, grupo, _codVendColumn, _grupoColumn);
        var json = await _executeQueryService.ExecuteAsync(datasetId, dax, cancellationToken).ConfigureAwait(false);
        return _parser.ParseFirstTable(json);
    }

    public async Task<PowerBiQueryTable> GetSolCategoriasAsync(
        string datasetId,
        int year,
        int month,
        DaxFilterValue? codVend = null,
        DaxFilterValue? grupo = null,
        CancellationToken cancellationToken = default)
    {
        var dax = DaxReportFactory.BuildSolCategorias(year, month, codVend, grupo, _codVendColumn, _grupoColumn);
        var json = await _executeQueryService.ExecuteAsync(datasetId, dax, cancellationToken).ConfigureAwait(false);
        return _parser.ParseFirstTable(json);
    }

    public async Task<PowerBiQueryTable> GetReporteDinamicoAsync(
        string datasetId,
        string reporteName,
        int year,
        int month,
        DaxFilterValue? codVend = null,
        DaxFilterValue? grupo = null,
        CancellationToken cancellationToken = default)
    {
        var dax = DaxReportFactory.BuildByReporteName(reporteName, year, month, codVend, grupo, _codVendColumn, _grupoColumn);
        var json = await _executeQueryService.ExecuteAsync(datasetId, dax, cancellationToken).ConfigureAwait(false);
        return _parser.ParseFirstTable(json);
    }

    public async Task<PowerBiQueryTable> ExecuteCustomQueryAsync(
        string datasetId,
        string dax,
        CancellationToken cancellationToken = default)
    {
        var json = await _executeQueryService.ExecuteAsync(datasetId, dax, cancellationToken).ConfigureAwait(false);
        return _parser.ParseFirstTable(json);
    }
}
