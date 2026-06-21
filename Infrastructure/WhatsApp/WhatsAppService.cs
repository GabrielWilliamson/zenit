using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Zenit.Infrastructure.WhatsApp;

/// <summary>
/// Cliente simple para API local de WhatsApp (Node.js).
/// </summary>
public sealed class WhatsAppService
{
    private readonly HttpClient _http;
    private readonly string _apiBaseUrl;
    private readonly string _sendMessagePath;
    private readonly string _sendFilePath;

    public WhatsAppService(HttpClient http, string? apiBaseUrl, string? sendMessagePath, string? sendFilePath)
    {
        _http = http;
        _apiBaseUrl = apiBaseUrl?.Trim() ?? string.Empty;
        _sendMessagePath = string.IsNullOrWhiteSpace(sendMessagePath) ? "/send-message" : sendMessagePath.Trim();
        _sendFilePath = string.IsNullOrWhiteSpace(sendFilePath) ? "/send-file" : sendFilePath.Trim();
    }

    public Task SendMessageAsync(
        string phoneNumber,
        string messageText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new ArgumentException("El numero destino es requerido.", nameof(phoneNumber));

        if (string.IsNullOrWhiteSpace(messageText))
            throw new ArgumentException("El mensaje es requerido.", nameof(messageText));

        var payload = new
        {
            phoneNumber = phoneNumber.Trim(),
            message = messageText.Trim()
        };

        return PostAsync(_sendMessagePath, payload, cancellationToken);
    }

    public Task SendFileAsync(
        string phoneNumber,
        string filePath,
        string? caption,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new ArgumentException("El numero destino es requerido.", nameof(phoneNumber));

        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("El path del archivo es requerido.", nameof(filePath));

        var payload = new
        {
            phoneNumber = phoneNumber.Trim(),
            filePath = filePath.Trim(),
            caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim()
        };

        return PostAsync(_sendFilePath, payload, cancellationToken);
    }

    private async Task PostAsync(string path, object payload, CancellationToken cancellationToken)
    {
        var uri = BuildUri(path);
        var body = JsonSerializer.Serialize(payload);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(uri, content, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"WhatsApp API error {(int)response.StatusCode} ({response.ReasonPhrase}): {error}");
        }
    }

    private Uri BuildUri(string path)
    {
        if (string.IsNullOrWhiteSpace(_apiBaseUrl))
        {
            throw new InvalidOperationException(
                "Configura WhatsApp:ApiBaseUrl en Settings de la aplicacion (ej: http://localhost:3000).");
        }

        if (!Uri.TryCreate(_apiBaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException(
                "WhatsApp:ApiBaseUrl no es una URL valida.");
        }

        var normalizedPath = path.Trim();
        if (!normalizedPath.StartsWith("/", StringComparison.Ordinal))
            normalizedPath = "/" + normalizedPath;

        return new Uri(baseUri, normalizedPath);
    }
}
