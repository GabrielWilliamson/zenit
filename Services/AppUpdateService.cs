using System.Reflection;
using Velopack;
using Velopack.Sources;
using Zenit.Models;
using Zenit.Properties;

namespace Zenit.Services;

public sealed class AppUpdateService
{
    private readonly Settings _settings;

    private sealed record UpdateContext(
        UpdateManager Manager,
        IUpdateSource Source,
        string FeedDescription);

    public AppUpdateService(Settings settings)
    {
        _settings = settings;
    }

    public AppUpdateStatus GetCurrentStatus()
    {
        var configuredFeedUrl = _settings.AppUpdateFeedUrl.Trim();
        if (string.IsNullOrWhiteSpace(configuredFeedUrl))
        {
            return CreateStatus(
                GetAssemblyVersion(),
                "Actualizaciones no configuradas",
                "Configura AppUpdateFeedUrl en Settings (URL del repo de GitHub o carpeta con releases.win.json).",
                "Actualizar no disponible",
                false,
                AppUpdateActionKind.None);
        }

        var feedWarning = GetFeedUrlWarning(configuredFeedUrl);
        var context = TryCreateUpdateContext();
        if (context is null)
        {
            return CreateStatus(
                GetAssemblyVersion(),
                "Feed invalido",
                feedWarning ?? "No se pudo interpretar AppUpdateFeedUrl.",
                "Actualizar no disponible",
                false,
                AppUpdateActionKind.None);
        }

        var manager = context.Manager;

        if (!manager.IsInstalled)
        {
            return CreateStatus(
                GetAssemblyVersion(),
                "Instalacion no compatible",
                BuildStatusMessage(
                    feedWarning,
                    "Las actualizaciones requieren instalar con el Setup de Velopack (no el ZIP portable ni dotnet run)."),
                "Actualizar no disponible",
                false,
                AppUpdateActionKind.None);
        }

        var currentVersion = manager.CurrentVersion?.ToString() ?? GetAssemblyVersion();
        var installationMode = manager.IsPortable ? "Portable (Velopack)" : "Instalacion (Velopack)";

        if (manager.UpdatePendingRestart is not null)
        {
            var pendingVersion = manager.UpdatePendingRestart.Version.ToString();
            return CreateStatus(
                currentVersion,
                installationMode,
                BuildStatusMessage(
                    feedWarning,
                    $"Hay una actualizacion descargada (v{pendingVersion}). Reinicia para instalarla.",
                    $"Feed: {context.FeedDescription}"),
                "Reiniciar e instalar",
                true,
                AppUpdateActionKind.ApplyPendingRestart);
        }

        return CreateStatus(
            currentVersion,
            installationMode,
            BuildStatusMessage(
                feedWarning,
                "Puedes buscar e instalar la ultima version disponible.",
                $"Feed: {context.FeedDescription}"),
            "Buscar actualizaciones",
            true,
            AppUpdateActionKind.CheckForUpdates);
    }

    public async Task<AppUpdateStatus> DownloadLatestUpdateAsync(Action<int> progress, CancellationToken cancellationToken = default)
    {
        var context = RequireUpdateContext();
        var manager = context.Manager;

        if (!manager.IsInstalled)
        {
            throw new InvalidOperationException(
                "Las actualizaciones solo estan disponibles cuando la app fue instalada con el Setup de Velopack.");
        }

        UpdateInfo? updates;

        try
        {
            updates = await manager.CheckForUpdatesAsync().WaitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                FormatUpdateError("No se pudo consultar el feed de actualizaciones", context.FeedDescription, exception),
                exception);
        }

        if (updates is null)
        {
            var currentVersion = manager.CurrentVersion?.ToString() ?? GetAssemblyVersion();
            return GetCurrentStatus() with
            {
                StatusMessage = $"Ya tienes la ultima version instalada (v{currentVersion})."
            };
        }

        var targetVersion = updates.TargetFullRelease.Version.ToString();

        try
        {
            await manager.DownloadUpdatesAsync(updates, progress, cancellationToken);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                FormatUpdateError($"No se pudo descargar la actualizacion v{targetVersion}", context.FeedDescription, exception),
                exception);
        }

        var downloadedStatus = GetCurrentStatus();
        if (downloadedStatus.ActionKind == AppUpdateActionKind.ApplyPendingRestart)
            return downloadedStatus;

        return downloadedStatus with
        {
            StatusMessage = $"Actualizacion v{targetVersion} descargada. Reinicia la app para instalarla."
        };
    }

    public void StartPendingUpdateAndRestart()
    {
        var manager = RequireUpdateContext().Manager;

        if (!manager.IsInstalled)
        {
            throw new InvalidOperationException(
                "Las actualizaciones solo estan disponibles cuando la app fue instalada con el Setup de Velopack.");
        }

        manager.ApplyUpdatesAndRestart(manager.UpdatePendingRestart);
    }

    private UpdateContext RequireUpdateContext()
    {
        return TryCreateUpdateContext()
            ?? throw new InvalidOperationException(
                "Las actualizaciones no estan configuradas. Agrega AppUpdateFeedUrl en Settings.");
    }

    private UpdateContext? TryCreateUpdateContext()
    {
        var feedUrl = _settings.AppUpdateFeedUrl.Trim();
        if (string.IsNullOrWhiteSpace(feedUrl))
            return null;

        if (!TryCreateUpdateSource(feedUrl, _settings.AppUpdateAccessToken.Trim(), out var source, out var feedDescription))
            return null;

        return new UpdateContext(new UpdateManager(source), source, feedDescription);
    }

    private static bool TryCreateUpdateSource(
        string feedUrl,
        string accessToken,
        out IUpdateSource source,
        out string feedDescription)
    {
        var normalizedFeedUrl = NormalizeFeedUrl(feedUrl);
        var token = string.IsNullOrWhiteSpace(accessToken) ? null : accessToken;
        feedDescription = DescribeFeedSource(feedUrl, accessToken);

        if (TryParseGithubRepoUrl(normalizedFeedUrl, out var githubRepoUrl))
        {
            source = new GithubSource(githubRepoUrl, token, prerelease: false);
            return true;
        }

        if (!Uri.TryCreate(normalizedFeedUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            source = null!;
            feedDescription = string.Empty;
            return false;
        }

        source = string.IsNullOrWhiteSpace(token)
            ? new SimpleWebSource(normalizedFeedUrl)
            : new SimpleWebSource(normalizedFeedUrl, new BearerTokenFileDownloader(token));

        return true;
    }

    private static string NormalizeFeedUrl(string feedUrl)
    {
        var normalized = feedUrl.Trim().TrimEnd('/');

        foreach (var suffix in new[]
                 {
                     "/releases.win.json",
                     "/releases.linux.json",
                     "/releases.osx.json",
                     "/RELEASES"
                 })
        {
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[..^suffix.Length].TrimEnd('/');
                break;
            }
        }

        return normalized;
    }

    private static bool TryParseGithubRepoUrl(string url, out string repoUrl)
    {
        repoUrl = string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var segments = uri.AbsolutePath
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 2)
            return false;

        repoUrl = $"https://github.com/{segments[0]}/{segments[1]}";
        return true;
    }

    private static string? GetFeedUrlWarning(string feedUrl)
    {
        var trimmed = feedUrl.Trim();

        if (trimmed.Contains("releases.win.json", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("releases.linux.json", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("releases.osx.json", StringComparison.OrdinalIgnoreCase))
        {
            return "AppUpdateFeedUrl debe ser la URL base (sin releases.win.json). Se corrigio automaticamente al buscar.";
        }

        return null;
    }

    private static string DescribeFeedSource(string feedUrl, string accessToken)
    {
        var normalized = NormalizeFeedUrl(feedUrl);

        if (TryParseGithubRepoUrl(normalized, out var githubRepoUrl))
        {
            var authSuffix = string.IsNullOrWhiteSpace(accessToken) ? "publico" : "con token";
            return $"GitHub ({githubRepoUrl}, {authSuffix})";
        }

        var auth = string.IsNullOrWhiteSpace(accessToken) ? "sin token" : "con token";
        return $"{normalized} ({auth})";
    }

    private static string FormatUpdateError(string prefix, string feedDescription, Exception exception)
    {
        var details = DescribeException(exception);
        return $"{prefix}. Feed: {feedDescription}. Detalle: {details}";
    }

    private static string DescribeException(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(current.Message))
                messages.Add(current.Message.Trim());
        }

        return messages.Count == 0
            ? exception.GetType().Name
            : string.Join(" -> ", messages.Distinct());
    }

    private static string BuildStatusMessage(params string?[] parts)
    {
        return string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static AppUpdateStatus CreateStatus(
        string currentVersion,
        string installationMode,
        string statusMessage,
        string actionButtonText,
        bool isActionEnabled,
        AppUpdateActionKind actionKind)
    {
        return new AppUpdateStatus(
            CurrentVersion: currentVersion,
            InstallationMode: installationMode,
            StatusMessage: statusMessage,
            ActionButtonText: actionButtonText,
            IsActionEnabled: isActionEnabled,
            ActionKind: actionKind);
    }

    private static string GetAssemblyVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
        return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }
}
