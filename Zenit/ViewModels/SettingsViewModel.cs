using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zenit.Core.Infrastructure.PowerBi.Models;
using Zenit.Properties;
using Zenit.Services;

namespace Zenit.ViewModels;

public partial class SettingsViewModel : ObservableRecipient
{
    private readonly PowerBiDefaultSelectionService _powerBiDefaultSelectionService;
    private bool _syncingSelection;

    public ObservableCollection<PowerBiWorkspace> WorkspaceOptions { get; } = new();
    public ObservableCollection<PowerBiDataset> DatasetOptions { get; } = new();

    [ObservableProperty] private string versionDescription = GetVersionDescription();
    [ObservableProperty] private string defaultWorkspaceId = string.Empty;
    [ObservableProperty] private string defaultDatasetId = string.Empty;
    [ObservableProperty] private PowerBiWorkspace? selectedWorkspace;
    [ObservableProperty] private PowerBiDataset? selectedDataset;
    [ObservableProperty] private string powerBiSettingsStatus = string.Empty;
    [ObservableProperty] private bool isPowerBiSettingsBusy;
    [ObservableProperty] private bool isLoadingWorkspaces;
    [ObservableProperty] private bool isLoadingDatasets;
    [ObservableProperty] private string connectionString = string.Empty;
    [ObservableProperty] private string powerBiTenantId = string.Empty;
    [ObservableProperty] private string powerBiClientId = string.Empty;
    [ObservableProperty] private string powerBiCodVendColumn = "VENDEDORES[COD_VEND]";
    [ObservableProperty] private string powerBiGrupoColumn = "VENDEDORES[GRUPO]";
    [ObservableProperty] private string powerBiNomVenColumn = "VENDEDORES[NOMVEN]";
    [ObservableProperty] private string whatsAppApiBaseUrl = string.Empty;
    [ObservableProperty] private string whatsAppSendMessagePath = "/api/whatsapp/messages/send";
    [ObservableProperty] private string whatsAppSendFilePath = "/api/whatsapp/files/send";
    [ObservableProperty] private string startupSettingsStatus = string.Empty;
    [ObservableProperty] private bool isSavingStartupSettings;

    public string ThemeDescription => "Tema claro fijo por defecto en Zenit.";

    public SettingsViewModel(PowerBiDefaultSelectionService powerBiDefaultSelectionService)
    {
        _powerBiDefaultSelectionService = powerBiDefaultSelectionService;
    }

    public async Task InitializeAsync()
    {
        LoadStartupSettings();

        var defaults = await _powerBiDefaultSelectionService.GetDefaultsAsync();
        DefaultWorkspaceId = defaults.DefaultWorkspaceId;
        DefaultDatasetId = defaults.DefaultDatasetId;

        PowerBiSettingsStatus = defaults.IsConfigured
            ? "Configuracion actual cargada."
            : "Aun no hay Workspace o Dataset predeterminado configurado.";

        await LoadPowerBiOptionsAsync();
    }

    partial void OnSelectedWorkspaceChanged(PowerBiWorkspace? value)
    {
        if (_syncingSelection)
            return;

        DefaultWorkspaceId = value?.Id ?? string.Empty;
        DefaultDatasetId = string.Empty;
        _ = LoadDatasetsForWorkspaceAsync(value?.Id, preferredDatasetId: null);
    }

    partial void OnSelectedDatasetChanged(PowerBiDataset? value)
    {
        if (_syncingSelection)
            return;

        DefaultDatasetId = value?.Id ?? string.Empty;
    }

    [RelayCommand]
    private async Task LoadPowerBiOptionsAsync()
    {
        try
        {
            IsLoadingWorkspaces = true;
            WorkspaceOptions.Clear();
            DatasetOptions.Clear();

            var workspaces = await _powerBiDefaultSelectionService.GetWorkspacesAsync();
            foreach (var workspace in workspaces)
                WorkspaceOptions.Add(workspace);

            if (WorkspaceOptions.Count == 0)
            {
                _syncingSelection = true;
                SelectedWorkspace = null;
                SelectedDataset = null;
                _syncingSelection = false;

                PowerBiSettingsStatus = "No se encontraron Workspaces disponibles para tu usuario.";
                return;
            }

            var workspaceToSelect = WorkspaceOptions.FirstOrDefault(w =>
                    string.Equals(w.Id, DefaultWorkspaceId, StringComparison.OrdinalIgnoreCase))
                ?? WorkspaceOptions.FirstOrDefault();

            _syncingSelection = true;
            SelectedWorkspace = workspaceToSelect;
            _syncingSelection = false;

            if (SelectedWorkspace != null)
            {
                DefaultWorkspaceId = SelectedWorkspace.Id;
                await LoadDatasetsForWorkspaceAsync(SelectedWorkspace.Id, DefaultDatasetId);
            }

            PowerBiSettingsStatus = "Selecciona Workspace y Dataset, luego guarda la configuracion.";
        }
        catch (Exception ex)
        {
            PowerBiSettingsStatus = $"No se pudo cargar Workspaces. Genera token en Home y reintenta. Detalle: {ex.Message}";
        }
        finally
        {
            IsLoadingWorkspaces = false;
        }
    }

    private async Task LoadDatasetsForWorkspaceAsync(string? workspaceId, string? preferredDatasetId)
    {
        DatasetOptions.Clear();

        _syncingSelection = true;
        SelectedDataset = null;
        _syncingSelection = false;

        if (string.IsNullOrWhiteSpace(workspaceId))
            return;

        try
        {
            IsLoadingDatasets = true;

            var datasets = await _powerBiDefaultSelectionService.GetDatasetsAsync(workspaceId);
            foreach (var dataset in datasets)
                DatasetOptions.Add(dataset);

            if (DatasetOptions.Count == 0)
            {
                DefaultDatasetId = string.Empty;
                PowerBiSettingsStatus = "El Workspace seleccionado no tiene Datasets disponibles.";
                return;
            }

            var datasetToSelect = DatasetOptions.FirstOrDefault(d =>
                    string.Equals(d.Id, preferredDatasetId, StringComparison.OrdinalIgnoreCase))
                ?? DatasetOptions.FirstOrDefault(d =>
                    string.Equals(d.Id, DefaultDatasetId, StringComparison.OrdinalIgnoreCase))
                ?? DatasetOptions.FirstOrDefault();

            _syncingSelection = true;
            SelectedDataset = datasetToSelect;
            _syncingSelection = false;

            if (SelectedDataset != null)
                DefaultDatasetId = SelectedDataset.Id;
        }
        catch (Exception ex)
        {
            PowerBiSettingsStatus = $"No se pudo cargar Datasets del Workspace seleccionado. Detalle: {ex.Message}";
        }
        finally
        {
            IsLoadingDatasets = false;
        }
    }

    [RelayCommand]
    private async Task SavePowerBiDefaultsAsync()
    {
        try
        {
            IsPowerBiSettingsBusy = true;

            var workspaceId = (SelectedWorkspace?.Id ?? DefaultWorkspaceId).Trim();
            var datasetId = (SelectedDataset?.Id ?? DefaultDatasetId).Trim();

            DefaultWorkspaceId = workspaceId;
            DefaultDatasetId = datasetId;

            await _powerBiDefaultSelectionService.SaveDefaultsAsync(workspaceId, datasetId);
            var status = await _powerBiDefaultSelectionService.EnsureSelectionStateAsync(resolveNames: false);

            PowerBiSettingsStatus = status.IsConfigured
                ? "Configuracion guardada correctamente."
                : "Debes completar DefaultWorkspaceId y DefaultDatasetId.";
        }
        catch (Exception ex)
        {
            PowerBiSettingsStatus = $"No se pudo guardar la configuracion: {ex.Message}";
        }
        finally
        {
            IsPowerBiSettingsBusy = false;
        }
    }

    [RelayCommand]
    private void SaveStartupSettings()
    {
        try
        {
            IsSavingStartupSettings = true;

            var settings = Settings.Default;
            settings.ConnectionString = ConnectionString.Trim();
            settings.PowerBiTenantId = PowerBiTenantId.Trim();
            settings.PowerBiClientId = PowerBiClientId.Trim();
            settings.PowerBiCodVendColumn = PowerBiCodVendColumn.Trim();
            settings.PowerBiGrupoColumn = PowerBiGrupoColumn.Trim();
            settings.PowerBiNomVenColumn = PowerBiNomVenColumn.Trim();
            settings.WhatsAppApiBaseUrl = WhatsAppApiBaseUrl.Trim();
            settings.WhatsAppSendMessagePath = WhatsAppSendMessagePath.Trim();
            settings.WhatsAppSendFilePath = WhatsAppSendFilePath.Trim();

            if (!settings.TryValidateRequiredSecrets(out var validationError))
            {
                StartupSettingsStatus = validationError;
                return;
            }

            settings.Save();
            StartupSettingsStatus = "Secretos guardados en Settings.Default. Reinicia la app para aplicar servicios ya abiertos.";
        }
        catch (Exception ex)
        {
            StartupSettingsStatus = $"No se pudieron guardar los secretos: {ex.Message}";
        }
        finally
        {
            IsSavingStartupSettings = false;
        }
    }

    private void LoadStartupSettings()
    {
        var settings = Settings.Default;
        ConnectionString = settings.ConnectionString;
        PowerBiTenantId = settings.PowerBiTenantId;
        PowerBiClientId = settings.PowerBiClientId;
        PowerBiCodVendColumn = settings.PowerBiCodVendColumn;
        PowerBiGrupoColumn = settings.PowerBiGrupoColumn;
        PowerBiNomVenColumn = settings.PowerBiNomVenColumn;
        WhatsAppApiBaseUrl = settings.WhatsAppApiBaseUrl;
        WhatsAppSendMessagePath = settings.WhatsAppSendMessagePath;
        WhatsAppSendFilePath = settings.WhatsAppSendFilePath;

        StartupSettingsStatus = settings.HasRequiredSecrets
            ? "Secretos cargados correctamente."
            : "Faltan secretos requeridos (Connection String, TenantId, ClientId o ApiBaseUrl).";
    }

    private static string GetVersionDescription()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
        return $"Zenit - {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }
}
