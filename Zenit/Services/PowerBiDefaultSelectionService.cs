using Zenit.Contracts.Services;
using Zenit.Core.Infrastructure.PowerBi.Datasets;
using Zenit.Core.Infrastructure.PowerBi.Models;
using Zenit.Core.Infrastructure.PowerBi.Workspaces;

namespace Zenit.Services;

public sealed class PowerBiDefaultSelectionService
{
    private const string DefaultWorkspaceIdKey = "DefaultWorkspaceId";
    private const string DefaultDatasetIdKey = "DefaultDatasetId";

    private readonly ILocalSettingsService _localSettingsService;
    private readonly PowerBiSelectionState _selectionState;
    private readonly WorkspaceService _workspaceService;
    private readonly DatasetService _datasetService;

    public PowerBiDefaultSelectionService(
        ILocalSettingsService localSettingsService,
        PowerBiSelectionState selectionState,
        WorkspaceService workspaceService,
        DatasetService datasetService)
    {
        _localSettingsService = localSettingsService;
        _selectionState = selectionState;
        _workspaceService = workspaceService;
        _datasetService = datasetService;
    }

    public async Task<PowerBiDefaults> GetDefaultsAsync()
    {
        var workspaceId = (await _localSettingsService.ReadSettingAsync<string>(DefaultWorkspaceIdKey))?.Trim() ?? string.Empty;
        var datasetId = (await _localSettingsService.ReadSettingAsync<string>(DefaultDatasetIdKey))?.Trim() ?? string.Empty;

        return new PowerBiDefaults
        {
            DefaultWorkspaceId = workspaceId,
            DefaultDatasetId = datasetId
        };
    }

    public async Task SaveDefaultsAsync(string? defaultWorkspaceId, string? defaultDatasetId)
    {
        var workspaceId = (defaultWorkspaceId ?? string.Empty).Trim();
        var datasetId = (defaultDatasetId ?? string.Empty).Trim();

        await _localSettingsService.SaveSettingAsync(DefaultWorkspaceIdKey, workspaceId);
        await _localSettingsService.SaveSettingAsync(DefaultDatasetIdKey, datasetId);
    }

    public async Task<IReadOnlyList<PowerBiWorkspace>> GetWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        return await _workspaceService.GetWorkspacesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PowerBiDataset>> GetDatasetsAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        return await _datasetService.GetDatasetsAsync(workspaceId, cancellationToken);
    }

    public async Task<PowerBiDefaultsStatus> EnsureSelectionStateAsync(bool resolveNames, CancellationToken cancellationToken = default)
    {
        var defaults = await GetDefaultsAsync();
        if (!defaults.IsConfigured)
        {
            _selectionState.SelectedWorkspace = null;
            _selectionState.SelectedDataset = null;

            return new PowerBiDefaultsStatus
            {
                IsConfigured = false,
                WorkspaceId = defaults.DefaultWorkspaceId,
                DatasetId = defaults.DefaultDatasetId
            };
        }

        var workspace = new PowerBiWorkspace
        {
            Id = defaults.DefaultWorkspaceId,
            Name = defaults.DefaultWorkspaceId
        };
        var dataset = new PowerBiDataset
        {
            Id = defaults.DefaultDatasetId,
            Name = defaults.DefaultDatasetId
        };

        string? warning = null;

        if (resolveNames)
        {
            try
            {
                var workspaces = await _workspaceService.GetWorkspacesAsync(cancellationToken);
                var configuredWorkspace = workspaces.FirstOrDefault(w =>
                    string.Equals(w.Id, defaults.DefaultWorkspaceId, StringComparison.OrdinalIgnoreCase));

                if (configuredWorkspace != null)
                {
                    workspace = configuredWorkspace;
                }
                else
                {
                    warning = "No se encontro el Workspace configurado en tu cuenta actual.";
                }

                var datasets = await _datasetService.GetDatasetsAsync(defaults.DefaultWorkspaceId, cancellationToken);
                var configuredDataset = datasets.FirstOrDefault(d =>
                    string.Equals(d.Id, defaults.DefaultDatasetId, StringComparison.OrdinalIgnoreCase));

                if (configuredDataset != null)
                {
                    dataset = configuredDataset;
                }
                else
                {
                    warning = warning ?? "No se encontro el Dataset configurado dentro del Workspace configurado.";
                }
            }
            catch (Exception ex)
            {
                warning = $"No se pudo validar Workspace/Dataset en Power BI: {ex.Message}";
            }
        }

        _selectionState.SelectedWorkspace = workspace;
        _selectionState.SelectedDataset = dataset;

        return new PowerBiDefaultsStatus
        {
            IsConfigured = true,
            WorkspaceId = workspace.Id,
            WorkspaceName = workspace.Name,
            DatasetId = dataset.Id,
            DatasetName = dataset.Name,
            WarningMessage = warning
        };
    }

    public sealed class PowerBiDefaults
    {
        public string DefaultWorkspaceId { get; init; } = string.Empty;
        public string DefaultDatasetId { get; init; } = string.Empty;
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(DefaultWorkspaceId)
            && !string.IsNullOrWhiteSpace(DefaultDatasetId);
    }

    public sealed class PowerBiDefaultsStatus
    {
        public bool IsConfigured { get; init; }
        public string WorkspaceId { get; init; } = string.Empty;
        public string WorkspaceName { get; init; } = string.Empty;
        public string DatasetId { get; init; } = string.Empty;
        public string DatasetName { get; init; } = string.Empty;
        public string? WarningMessage { get; init; }
    }
}
