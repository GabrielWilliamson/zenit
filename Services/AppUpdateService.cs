using System.Reflection;
using Velopack;
using Velopack.Sources;
using Zenit.Models;
using Zenit.Properties;

namespace Zenit.Services;

public sealed class AppUpdateService
{
    private readonly Settings _settings;

    public AppUpdateService(Settings settings)
    {
        _settings = settings;
    }

    public AppUpdateStatus GetCurrentStatus()
    {
        var manager = TryCreateUpdateManager();
        if (manager is null)
        {
            return CreateStatus(
                GetAssemblyVersion(),
                "Actualizaciones no configuradas",
                "Configura AppUpdateFeedUrl en Settings para habilitar actualizaciones.",
                "Actualizar no disponible",
                false,
                AppUpdateActionKind.None);
        }

        if (!manager.IsInstalled)
        {
            return CreateStatus(
                GetAssemblyVersion(),
                "Modo desarrollo",
                "Las actualizaciones automaticas solo estan disponibles en builds instalados con Velopack.",
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
                $"Hay una actualizacion descargada (v{pendingVersion}). Reinicia para instalarla.",
                "Reiniciar e instalar",
                true,
                AppUpdateActionKind.ApplyPendingRestart);
        }

        return CreateStatus(
            currentVersion,
            installationMode,
            "Puedes buscar e instalar la ultima version disponible.",
            "Buscar actualizaciones",
            true,
            AppUpdateActionKind.CheckForUpdates);
    }

    public async Task<AppUpdateStatus> DownloadLatestUpdateAsync(Action<int> progress, CancellationToken cancellationToken = default)
    {
        var manager = TryCreateUpdateManager()
            ?? throw new InvalidOperationException("Las actualizaciones no estan configuradas.");

        if (!manager.IsInstalled)
            throw new InvalidOperationException("Las actualizaciones solo estan disponibles en builds instalados con Velopack.");

        var updates = await manager.CheckForUpdatesAsync().WaitAsync(cancellationToken);
        if (updates is null)
        {
            return GetCurrentStatus() with
            {
                StatusMessage = "Ya tienes la ultima version instalada."
            };
        }

        await manager.DownloadUpdatesAsync(updates, progress, cancellationToken);
        return GetCurrentStatus();
    }

    public void StartPendingUpdateAndRestart()
    {
        var manager = TryCreateUpdateManager()
            ?? throw new InvalidOperationException("Las actualizaciones no estan configuradas.");

        if (!manager.IsInstalled)
            throw new InvalidOperationException("Las actualizaciones solo estan disponibles en builds instalados con Velopack.");

        manager.ApplyUpdatesAndRestart(manager.UpdatePendingRestart);
    }

    private UpdateManager? TryCreateUpdateManager()
    {
        var feedUrl = _settings.AppUpdateFeedUrl.Trim();
        if (string.IsNullOrWhiteSpace(feedUrl))
            return null;

        IUpdateSource source;
        var accessToken = _settings.AppUpdateAccessToken.Trim();
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            source = new SimpleWebSource(feedUrl, new BearerTokenFileDownloader(accessToken));
        }
        else
        {
            source = new SimpleWebSource(feedUrl);
        }

        return new UpdateManager(source);
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
