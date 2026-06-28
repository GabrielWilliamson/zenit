using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zenit.Models;
using Zenit.Services;

namespace Zenit.ViewModels;

/// <summary>
/// Pantalla "Power BI Token": permite crear/validar el token y mostrar
/// la configuracion predeterminada de Power BI usada por el resto de modulos.
/// </summary>
public partial class HomeViewModel : ObservableRecipient
{
    private readonly TokenManager _tokenManager;
    private readonly PowerBiDefaultSelectionService _defaultSelectionService;
    private readonly AppUpdateService _appUpdateService;
    private AppUpdateActionKind _updateActionKind;
    private bool _isUpdating;
    private bool _updateActionFailed;
    private AppUpdateStatus _currentUpdateStatus = new(
        CurrentVersion: "desconocida",
        InstallationMode: string.Empty,
        StatusMessage: string.Empty,
        ActionButtonText: "Actualizar no disponible",
        IsActionEnabled: false,
        ActionKind: AppUpdateActionKind.None);

    [ObservableProperty]
    private string status = "Sin token";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string powerBiConfigStatus = "Configuracion de Power BI no cargada.";

    [ObservableProperty]
    private string activeWorkspace = string.Empty;

    [ObservableProperty]
    private string activeDataset = string.Empty;

    [ObservableProperty]
    private bool isPowerBiConfigured;

    [ObservableProperty]
    private string deviceCodeInstructions = string.Empty;

    [ObservableProperty]
    private string currentVersion = "desconocida";

    [ObservableProperty]
    private string installationMode = string.Empty;

    [ObservableProperty]
    private string updateStatusMessage = string.Empty;

    [ObservableProperty]
    private string updateActionButtonText = "Actualizar no disponible";

    [ObservableProperty]
    private bool isUpdateActionEnabled;

    public bool CanGenerateToken => !IsBusy;

    public HomeViewModel(
        TokenManager tokenManager,
        PowerBiDefaultSelectionService defaultSelectionService,
        AppUpdateService appUpdateService)
    {
        _tokenManager = tokenManager;
        _defaultSelectionService = defaultSelectionService;
        _appUpdateService = appUpdateService;
        ApplyUpdateStatus(_appUpdateService.GetCurrentStatus());
    }

    public async Task InitializeAsync()
    {
        DeviceCodeInstructions = string.Empty;
        ApplyUpdateStatus(_appUpdateService.GetCurrentStatus());
        await RefreshTokenStatusAsync();
        await RefreshPowerBiConfigurationAsync();
    }

    /// <summary>
    /// Intenta leer un token vigente de la base. Si existe, la UI lo muestra.
    /// </summary>
    public async Task RefreshTokenStatusAsync()
    {
        try
        {
            IsBusy = true;

            // Si hay token valido en la base, no lanza excepcion.
            await _tokenManager.GetValidTokenAsync();
            Status = "Token vigente";
        }
        catch
        {
            Status = "Sin token";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RefreshPowerBiConfigurationAsync()
    {
        var config = await _defaultSelectionService.EnsureSelectionStateAsync(resolveNames: true);

        if (!config.IsConfigured)
        {
            IsPowerBiConfigured = false;
            ActiveWorkspace = string.Empty;
            ActiveDataset = string.Empty;
            PowerBiConfigStatus = "Falta configurar Workspace y Dataset en Settings.";
            return;
        }

        IsPowerBiConfigured = true;
        ActiveWorkspace = BuildDisplay(config.WorkspaceName, config.WorkspaceId);
        ActiveDataset = BuildDisplay(config.DatasetName, config.DatasetId);

        PowerBiConfigStatus = string.IsNullOrWhiteSpace(config.WarningMessage)
            ? "Configuracion de Power BI activa."
            : $"Configuracion guardada, pero no se pudieron validar nombres: {config.WarningMessage}";
    }

    /// <summary>
    /// Inicia el flujo de login (Device Code) y guarda el token.
    /// </summary>
    public async Task GenerateTokenAsync(Action<string> deviceCodeCallback)
    {
        if (deviceCodeCallback is null)
            throw new ArgumentNullException(nameof(deviceCodeCallback));

        try
        {
            IsBusy = true;
            Status = "Autenticando con Power BI...";
            DeviceCodeInstructions = "Esperando instrucciones de Microsoft...";

            await _tokenManager.GetValidTokenAsync(deviceCodeCallback);

            Status = "Token listo y guardado";
            await RefreshPowerBiConfigurationAsync();
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
            DeviceCodeInstructions = $"No se pudo iniciar el login. {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => InitializeAsync();

    [RelayCommand(CanExecute = nameof(CanGenerateToken))]
    private async Task GenerateTokenWithDeviceCodeAsync()
    {
        await GenerateTokenAsync(message => DeviceCodeInstructions = message);
    }

    [RelayCommand(CanExecute = nameof(CanRunUpdateAction))]
    private async Task RunUpdateActionAsync()
    {
        if (_isUpdating)
            return;

        _updateActionFailed = false;

        try
        {
            switch (_updateActionKind)
            {
                case AppUpdateActionKind.CheckForUpdates:
                    _isUpdating = true;
                    IsUpdateActionEnabled = false;
                    UpdateStatusMessage = "Buscando actualizaciones...";

                    var updateStatus = await _appUpdateService.DownloadLatestUpdateAsync(progress =>
                    {
                        UpdateStatusMessage = $"Descargando actualizacion... {progress}%";
                    });

                    ApplyUpdateStatus(updateStatus);
                    break;

                case AppUpdateActionKind.ApplyPendingRestart:
                    IsUpdateActionEnabled = false;
                    UpdateStatusMessage = "Cerrando la aplicacion para instalar la actualizacion...";
                    _appUpdateService.StartPendingUpdateAndRestart();
                    break;
            }
        }
        catch (Exception exception)
        {
            _updateActionFailed = true;
            UpdateStatusMessage = exception.Message;
            ApplyUpdateStatus(_appUpdateService.GetCurrentStatus(), preserveStatusMessage: true);
        }
        finally
        {
            _isUpdating = false;

            if (!_updateActionFailed && _updateActionKind != AppUpdateActionKind.ApplyPendingRestart)
                ApplyUpdateStatus(_currentUpdateStatus);
        }
    }

    private bool CanRunUpdateAction() => IsUpdateActionEnabled && !_isUpdating;

    private void ApplyUpdateStatus(AppUpdateStatus status, bool preserveStatusMessage = false)
    {
        _currentUpdateStatus = status;
        _updateActionKind = status.ActionKind;
        CurrentVersion = status.CurrentVersion;
        InstallationMode = status.InstallationMode;

        if (!preserveStatusMessage)
            UpdateStatusMessage = status.StatusMessage;

        UpdateActionButtonText = status.ActionButtonText;
        IsUpdateActionEnabled = status.IsActionEnabled && !_isUpdating;
        RunUpdateActionCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsUpdateActionEnabledChanged(bool value)
    {
        RunUpdateActionCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        GenerateTokenWithDeviceCodeCommand.NotifyCanExecuteChanged();
    }

    private static string BuildDisplay(string name, string id)
    {
        if (string.IsNullOrWhiteSpace(name))
            return id;

        return string.Equals(name, id, StringComparison.Ordinal)
            ? id
            : $"{name} ({id})";
    }
}
