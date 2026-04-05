using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Zenit.Core.Services;

namespace Zenit.Core.Infrastructure.PowerBi.Queries;

/// <summary>
/// Ejecuta DAX contra Power BI usando el endpoint datasets/{id}/executeQueries.
///
/// Notas de rendimiento:
/// - Usamos HttpRequestMessage con headers por-request (evita mutar DefaultRequestHeaders).
/// - Usamos ConfigureAwait(false) porque este servicio vive en una librería (no necesita el hilo UI).
/// - Serializamos el body directo a UTF8 para evitar strings intermedias grandes.
/// </summary>
public sealed class ExecuteQueryService
{
    private readonly HttpClient _http;
    private readonly TokenManager _tokenManager;

    public ExecuteQueryService(HttpClient http, TokenManager tokenManager)
    {
        _http = http;
        _tokenManager = tokenManager;
    }

    public async Task<string> ExecuteAsync(string datasetId, string dax, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(datasetId))
            throw new ArgumentException("datasetId es requerido");

        if (!dax.TrimStart().StartsWith("EVALUATE", StringComparison.OrdinalIgnoreCase))
            throw new Exception("El DAX debe comenzar con EVALUATE");

        // IMPORTANT: evitar reanudar en el hilo de UI (WinUI/Dispatcher).
        // Así, el parsing y otras operaciones posteriores no “pegan” la app.
        var token = await _tokenManager.GetValidTokenAsync().ConfigureAwait(false);

        var body = new
        {
            queries = new[]
            {
                new { query = dax }
            }
        };

        // Evitamos string intermedia grande (JSON) y la conversión extra a bytes.
        var payload = JsonSerializer.SerializeToUtf8Bytes(body);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.powerbi.com/v1.0/myorg/datasets/{datasetId}/executeQueries");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new ByteArrayContent(payload);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };

        using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new Exception($"ExecuteQueries error {(int)response.StatusCode}: {error}");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }
}
