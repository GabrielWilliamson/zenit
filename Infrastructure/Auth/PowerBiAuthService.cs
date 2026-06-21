using System;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace Zenit.Infrastructure.Auth;

public class PowerBiAuthService
{
    private static readonly string[] Scopes =
    {
        "https://analysis.windows.net/powerbi/api/.default"
    };

    private readonly string _tenantId;
    private readonly string _clientId;
    private IPublicClientApplication? _app;

    public PowerBiAuthService(string tenantId, string clientId)
    {
        _tenantId = (tenantId ?? string.Empty).Trim();
        _clientId = (clientId ?? string.Empty).Trim();
    }

    public async Task<AuthenticationResult> AcquireTokenAsync(Action<string> deviceCodeCallback)
    {
        return await GetApplication()
            .AcquireTokenWithDeviceCode(Scopes, code =>
            {
                deviceCodeCallback(code.Message);
                return Task.CompletedTask;
            })
            .ExecuteAsync();
    }

    private IPublicClientApplication GetApplication()
    {
        if (string.IsNullOrWhiteSpace(_tenantId))
            throw new InvalidOperationException("Falta configurar Power BI TenantId en Settings.");

        if (string.IsNullOrWhiteSpace(_clientId))
            throw new InvalidOperationException("Falta configurar Power BI ClientId en Settings.");

        _app ??= PublicClientApplicationBuilder
            .Create(_clientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, _tenantId)
            .WithRedirectUri("https://login.microsoftonline.com/common/oauth2/nativeclient")
            .Build();

        return _app;
    }
}
