using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Zenit.Infrastructure.PowerBi.Models;
using Zenit.Services;

namespace Zenit.Infrastructure.PowerBi.Datasets;

public class DatasetService
{
    private readonly HttpClient _http;
    private readonly TokenManager _tokenManager;

    public DatasetService(HttpClient http, TokenManager tokenManager)
    {
        _http = http;
        _tokenManager = tokenManager;
    }

    /// <summary>
    /// Lista los datasets de un workspace.
    /// </summary>
    public async Task<List<PowerBiDataset>> GetDatasetsAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
            throw new ArgumentException("workspaceId es requerido", nameof(workspaceId));

        var token = await _tokenManager.GetValidTokenAsync().ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.powerbi.com/v1.0/myorg/groups/{workspaceId}/datasets");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new Exception($"Power BI error {(int)response.StatusCode}: {error}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var list = new List<PowerBiDataset>();

        foreach (var item in doc.RootElement.GetProperty("value").EnumerateArray())
        {
            list.Add(new PowerBiDataset
            {
                Id = item.GetProperty("id").GetString()!,
                Name = item.GetProperty("name").GetString()!
            });
        }

        return list;
    }
}
