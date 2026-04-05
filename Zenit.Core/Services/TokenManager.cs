using System;
using System.Threading;
using System.Threading.Tasks;
using Zenit.Core.Domain.Entities;
using Zenit.Core.Infrastructure.Auth;
using Zenit.Core.Infrastructure.Persistence;

namespace Zenit.Core.Services;

/// <summary>
/// Administra el token de Power BI.
///
/// Idea clave:
/// - La UI (Home) puede iniciar sesión (Device Code) y guardar el token.
/// - El resto de la app (workspaces/datasets/reportes) SOLO consume un token ya existente.
///
/// Notas de rendimiento:
/// - Todo es async/await y usamos ConfigureAwait(false) en capa Core para evitar capturar el hilo UI.
/// - TokenEntity usa un "leeway" de 5 minutos en IsExpired() para evitar que el token caduque en medio de una llamada.
/// </summary>
public sealed class TokenManager
{
    private readonly TokenRepository _repository;
    private readonly PowerBiAuthService _authService;

    public TokenManager(TokenRepository repository, PowerBiAuthService authService)
    {
        _repository = repository;
        _authService = authService;
    }

    /// <summary>
    /// Usado en Home (login): obtiene token vigente o dispara el flujo Device Code.
    /// </summary>
    public async Task<string> GetValidTokenAsync(Action<string> deviceCodeCallback, CancellationToken cancellationToken = default)
    {
        if (deviceCodeCallback is null)
            throw new ArgumentNullException(nameof(deviceCodeCallback));

        var token = await _repository.GetLastValidAsync(cancellationToken).ConfigureAwait(false);
        if (token != null && !token.IsExpired())
            return token.AccessToken;

        // MSAL: espera a que el usuario complete el login en el navegador.
        var result = await _authService.AcquireTokenAsync(deviceCodeCallback).ConfigureAwait(false);

        var newToken = new TokenEntity
        {
            AccessToken = result.AccessToken,
            ExpiresAtUtc = result.ExpiresOn.UtcDateTime
        };

        await _repository.SaveAsync(newToken, cancellationToken).ConfigureAwait(false);
        return newToken.AccessToken;
    }

    /// <summary>
    /// Usado por los servicios de Power BI (datasets/reportes/workspaces).
    /// Si no hay token, obliga al usuario a iniciar sesión primero.
    /// </summary>
    public async Task<string> GetValidTokenAsync(CancellationToken cancellationToken = default)
    {
        var token = await _repository.GetLastValidAsync(cancellationToken).ConfigureAwait(false);

        if (token == null || token.IsExpired())
            throw new InvalidOperationException("No hay token válido. Ve a 'Power BI Token' y genera uno primero.");

        return token.AccessToken;
    }
}
