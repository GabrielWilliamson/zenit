using System.Net.Http.Headers;
using Velopack.Sources;

namespace Zenit.Services;

internal sealed class BearerTokenFileDownloader(string accessToken) : HttpClientFileDownloader
{
    protected override HttpClient CreateHttpClient(IDictionary<string, string>? headers, double timeout)
    {
        var client = base.CreateHttpClient(headers, timeout);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}
