using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Zenit.Core.Infrastructure.PowerBi.Queries;
using Zenit.Core.Infrastructure.PowerBi.Reports;

namespace Zenit.Core.Infrastructure.PowerBi.Dimensions;

/// <summary>
/// Carga valores (distinct) de columnas del modelo vía ExecuteQueries.
/// Esto permite poblar dropdowns (COD_VEND, GRUPO, etc.) sin "inventar" tipos.
///
/// Ventajas:
/// - Evita errores Text vs Integer
/// - Respeta ceros a la izquierda (018)
/// - UX: el usuario selecciona de una lista real del dataset
///
/// Incluye cache simple en memoria por dataset+columna (TTL).
/// </summary>
public sealed class DimensionValuesService
{
    private readonly ExecuteQueryService _executeQuery;
    private readonly ExecuteQueriesResponseParser _parser;

    // Cache estático para que funcione incluso si este servicio es Scoped.
    // Key: "{datasetId}|{columnRef}"
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new();

    // TTL razonable: evita spamear la API cada vez que entras a Reportes.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    // Cache separado para mapas (COD_VEND -> NOMVEN)
    private static readonly ConcurrentDictionary<string, VendorMapCacheEntry> VendorMapCache = new();

    public DimensionValuesService(ExecuteQueryService executeQuery, ExecuteQueriesResponseParser parser)
    {
        _executeQuery = executeQuery;
        _parser = parser;
    }

    public async Task<IReadOnlyList<DaxFilterValue>> GetDistinctValuesAsync(
        string datasetId,
        string columnRef,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(datasetId)) throw new ArgumentException("datasetId es requerido");
        if (string.IsNullOrWhiteSpace(columnRef)) throw new ArgumentException("columnRef es requerido");

        var key = $"{datasetId}|{columnRef}";

        // Cache hit
        if (Cache.TryGetValue(key, out var entry) && !entry.IsExpired())
            return entry.Values;

        var dax = BuildDistinctValuesDax(columnRef);

        var json = await _executeQuery.ExecuteAsync(datasetId, dax, cancellationToken).ConfigureAwait(false);
        var table = _parser.ParseFirstTable(json);

        // Esperamos una sola columna llamada VALUE (pero toleramos otras).
        var values = new List<DaxFilterValue>(capacity: Math.Max(16, table.Rows.Count));

        foreach (var row in table.Rows)
        {
            if (!row.TryGetValue("VALUE", out var raw) || raw is null)
            {
                // fallback: primer valor disponible
                raw = row.Values.FirstOrDefault(v => v is not null);
            }

            if (raw is null) continue;

            var filter = DaxFilterValue.FromRaw(raw);
            // Excluir vacíos ("" o null)
            if (string.IsNullOrWhiteSpace(filter.Display))
                continue;

            values.Add(filter);
        }

        // Orden: si todos son numéricos -> ordenar numérico, si no -> alfabético
        var allNumeric = values.All(v => v.Raw is int or long or short or byte or double or float or decimal);
        IReadOnlyList<DaxFilterValue> ordered = allNumeric
            ? values.OrderBy(v => Convert.ToDouble(v.Raw)).ToList()
            : values.OrderBy(v => v.Display, StringComparer.CurrentCultureIgnoreCase).ToList();

        Cache[key] = new CacheEntry(ordered);
        return ordered;
    }

    /// <summary>
    /// Obtiene un mapa COD_VEND -> NOMVEN desde el dataset.
    ///
    /// Se usa para mejorar la UX (mostrar nombre del vendedor) y para encabezados del PDF.
    /// Si el modelo no tiene NOMVEN o hay un error, el caller puede hacer fallback.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetVendorNameMapAsync(
        string datasetId,
        string codVendColumnRef,
        string nomVenColumnRef,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(datasetId)) throw new ArgumentException("datasetId es requerido");
        if (string.IsNullOrWhiteSpace(codVendColumnRef)) throw new ArgumentException("codVendColumnRef es requerido");
        if (string.IsNullOrWhiteSpace(nomVenColumnRef)) throw new ArgumentException("nomVenColumnRef es requerido");

        var key = $"{datasetId}|MAP|{codVendColumnRef}|{nomVenColumnRef}";
        if (VendorMapCache.TryGetValue(key, out var mapEntry) && !mapEntry.IsExpired())
            return mapEntry.Map;

        var dax =
            "EVALUATE\n" +
            "VAR __t = SUMMARIZECOLUMNS(\n" +
            $"    {codVendColumnRef},\n" +
            $"    {nomVenColumnRef}\n" +
            ")\n" +
            "RETURN\n" +
            "FILTER(\n" +
            "    SELECTCOLUMNS(__t, \"COD_VEND\", " + codVendColumnRef + ", \"NOMVEN\", " + nomVenColumnRef + "),\n" +
            "    LEN([COD_VEND] & \"\") > 0 && LEN([NOMVEN] & \"\") > 0\n" +
            ")\n" +
            "ORDER BY [COD_VEND]";

        var json = await _executeQuery.ExecuteAsync(datasetId, dax, cancellationToken).ConfigureAwait(false);
        var table = _parser.ParseFirstTable(json);

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in table.Rows)
        {
            row.TryGetValue("COD_VEND", out var codeRaw);
            row.TryGetValue("NOMVEN", out var nameRaw);
            if (codeRaw is null || nameRaw is null) continue;

            var code = DaxFilterValue.FromRaw(codeRaw).Display;
            var name = nameRaw.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name)) continue;

            dict[code] = name;
        }

        VendorMapCache[key] = new VendorMapCacheEntry(dict);

        return dict;
    }

    private sealed record VendorMapCacheEntry(IReadOnlyDictionary<string, string> Map)
    {
        private readonly DateTime _createdUtc = DateTime.UtcNow;
        public bool IsExpired() => DateTime.UtcNow - _createdUtc > CacheTtl;
    }

    /// <summary>
    /// Construye un DAX que devuelve valores distintos de una columna.
    ///
    /// Nota: usamos LEN([VALUE] & "") para filtrar nulos/blancos de forma segura:
    /// - Si VALUE es número, VALUE & "" lo convierte a texto.
    /// - Si es texto, funciona igual.
    /// </summary>
    private static string BuildDistinctValuesDax(string columnRef)
    {
        // ColumnRef se espera con formato DAX: TABLA[COLUMNA]
        return
            "EVALUATE\n" +
            $"VAR __v = SELECTCOLUMNS(VALUES({columnRef}), \"VALUE\", {columnRef})\n" +
            "RETURN\n" +
            "FILTER(__v, LEN([VALUE] & \"\") > 0)\n" +
            "ORDER BY [VALUE]";
    }

    private sealed record CacheEntry(IReadOnlyList<DaxFilterValue> Values)
    {
        private readonly DateTime _createdUtc = DateTime.UtcNow;

        public bool IsExpired() => DateTime.UtcNow - _createdUtc > CacheTtl;
    }
}
