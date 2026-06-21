using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Zenit.Infrastructure.PowerBi.Models;
using Zenit.Services;

namespace Zenit.Infrastructure.PowerBi.Workspaces;

public class WorkspaceService
{
    private readonly HttpClient _http;
    private readonly TokenManager _tokenManager;

    public WorkspaceService(HttpClient http, TokenManager tokenManager)
    {
        _http = http;
        _tokenManager = tokenManager;
    }

    /// <summary>
    /// Lista los workspaces ("groups") del usuario.
    /// Se implementa con HttpRequestMessage para no mutar DefaultRequestHeaders del HttpClient
    /// (evita condiciones de carrera si en el futuro haces llamadas concurrentes).
    /// </summary>
    public async Task<List<PowerBiWorkspace>> GetWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        var token = await _tokenManager.GetValidTokenAsync().ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.powerbi.com/v1.0/myorg/groups");
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

        var list = new List<PowerBiWorkspace>();

        foreach (var item in doc.RootElement.GetProperty("value").EnumerateArray())
        {
            list.Add(new PowerBiWorkspace
            {
                Id = item.GetProperty("id").GetString()!,
                Name = item.GetProperty("name").GetString()!
            });
        }

        return list;
    }
}
