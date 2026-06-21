using System.Text.Json;
using Zenit.Models.SalaryPlans.Responses;
using Zenit.Mappers;
using Zenit.Models.SalaryPlans.Entities;

namespace Zenit.Services.SalaryPlans;

public sealed class PowerBiDataService
{
    private readonly IPowerBiQueryService _queryService;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public PowerBiDataService(IPowerBiQueryService queryService)
    {
        _queryService = queryService;
    }

    public async Task<IReadOnlyList<Medicion>> GetMedicionesAsync(string datasetId)
    {
        const string dax = "EVALUATE ('MEDICIONES')";
        var rows = await ExecuteRowsAsync(datasetId, dax);
        return rows.Select(PowerBiRowMapper.ToMedicion).ToList();
    }

    public async Task<IReadOnlyList<MedicionMeta>> GetMedicionesMetasAsync(string datasetId, int top = 100)
    {
        var dax = $"EVALUATE TOPN({top}, 'MEDICIONES_METAS')";
        var rows = await ExecuteRowsAsync(datasetId, dax);
        return rows.Select(PowerBiRowMapper.ToMedicionMeta).ToList();
    }

    public async Task<IReadOnlyList<TiempoItem>> GetTiempoAsync(string datasetId, int top = 100)
    {
        var dax = $"EVALUATE TOPN({top}, 'TIEMPO')";
        var rows = await ExecuteRowsAsync(datasetId, dax);
        return rows.Select(PowerBiRowMapper.ToTiempoItem).ToList();
    }

    public async Task<IReadOnlyList<ProductoMarca>> GetProductosMarcasAsync(string datasetId, int top = 100)
    {
        var dax = $"EVALUATE TOPN({top}, 'PRODUCTOS')";
        var rows = await ExecuteRowsAsync(datasetId, dax);
        return rows.Select(PowerBiRowMapper.ToProductoMarca).ToList();
    }

    public async Task<IReadOnlyList<string>> GetMarcasDistinctAsync(string datasetId, int top = 500)
    {
        var productos = await GetProductosMarcasAsync(datasetId, top);
        return productos
            .Where(x => !string.IsNullOrWhiteSpace(x.Marca))
            .Select(x => x.Marca.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
    }

    public async Task<IReadOnlyList<VendedorInfo>> GetVendedoresAsync(string datasetId, int top = 100)
    {
        var dax = $"EVALUATE TOPN({top}, 'VENDEDORES')";
        var rows = await ExecuteRowsAsync(datasetId, dax);
        return rows.Select(PowerBiRowMapper.ToVendedorInfo).ToList();
    }

    public async Task<IReadOnlyList<ReporteInfo>> GetReportesAsync(string datasetId, int top = 100)
    {
        var dax = $"EVALUATE TOPN({top}, 'REPORTES')";
        var rows = await ExecuteRowsAsync(datasetId, dax);
        return rows.Select(PowerBiRowMapper.ToReporteInfo).ToList();
    }

    public async Task<bool> ExistsReporteMetasVentasAsync(string datasetId)
    {
        var reportes = await GetReportesAsync(datasetId);
        return reportes.Any(x => string.Equals(x.Reporte, "METAS_VENTAS", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<MedicionMeta>> GetMetasVentasAsync(string datasetId, int top = 500)
    {
        var metas = await GetMedicionesMetasAsync(datasetId, top);

        return metas
            .Where(x => string.Equals(x.NombreReporte, "METAS_VENTAS", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<IReadOnlyList<MedicionMeta>> GetMetasVentasPorVendedorAsync(string datasetId, string codVend, int top = 1000)
    {
        var metas = await GetMetasVentasAsync(datasetId, top);

        return metas
            .Where(x => string.Equals(x.CodVend?.Trim(), codVend?.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private async Task<List<Dictionary<string, JsonElement>>> ExecuteRowsAsync(string datasetId, string dax)
    {
        var json = await _queryService.ExecuteAsync(datasetId, dax);

        var response = JsonSerializer.Deserialize<PowerBiQueryResponse>(json, _jsonOptions)
                       ?? throw new InvalidOperationException("No se pudo deserializar la respuesta de Power BI.");

        return response.Results
            .SelectMany(r => r.Tables)
            .SelectMany(t => t.Rows)
            .ToList();
    }
}
