using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zenit.Helpers;
using Zenit.Infrastructure.Logging;
using Zenit.Models;
using Zenit.Models.CustomReports;
using Zenit.Services;
using Microsoft.UI.Xaml.Controls;

namespace Zenit.ViewModels;

public sealed partial class CustomReportRunnerViewModel : ObservableRecipient
{
    private readonly DynamicReportService _dynamicReportService;
    private readonly PowerBiSelectionState _selectionState;
    private readonly PowerBiDefaultSelectionService _defaultSelectionService;
    private readonly ILogger<CustomReportRunnerViewModel> _logger;
    private bool _initialized;
    private string? _filtersLoadedForDatasetId;

    public ObservableCollection<ReportTemplate> Templates { get; } = new();
    public ObservableCollection<FilterOption> VendorOptions { get; } = new();
    public ObservableCollection<FilterOption> SelectedVendors { get; } = new();
    public ObservableCollection<FilterOption> GroupOptions { get; } = new();
    public ObservableCollection<MonthOption> Months { get; } = new();
    public ObservableCollection<int> Years { get; } = new();
    public ObservableCollection<ReportColumnDefinition> ResultColumns { get; } = new();
    public ObservableCollection<CustomReportRuleResult> RuleResults { get; } = new();
    public ObservableCollection<ExecutionSummaryItem> SummaryItems { get; } = new();
    public ObservableCollection<string> ExecutionWarnings { get; } = new();

    [ObservableProperty] private ReportTemplate? selectedTemplate;
    [ObservableProperty] private MonthOption? selectedMonth;
    [ObservableProperty] private int selectedYear;
    [ObservableProperty] private FilterOption? selectedGroup;
    [ObservableProperty] private string datasetName = string.Empty;
    [ObservableProperty] private string datasetId = string.Empty;
    [ObservableProperty] private string vendorSelectionText = "Seleccionar vendedores";
    [ObservableProperty] private string templateSourceText = "Sin plantilla seleccionada";
    [ObservableProperty] private string templateColumnsText = "Las columnas visibles apareceran aqui al elegir una plantilla.";
    [ObservableProperty] private string templateRulesText = "Las reglas configuradas se mostraran al ejecutar.";
    [ObservableProperty] private bool isGroupAvailable = true;
    [ObservableProperty] private bool isFiltersLoading;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private IEnumerable? executionRows;
    [ObservableProperty] private bool isStatusOpen;
    [ObservableProperty] private InfoBarSeverity statusSeverity = InfoBarSeverity.Informational;
    [ObservableProperty] private string statusTitle = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string selectedVendorsCsv = string.Empty;
    [ObservableProperty] private string executionPreview = "Todavia no hay resultados.";

    public bool CanRun => !IsBusy
                          && SelectedTemplate != null
                          && SelectedMonth != null
                          && SelectedYear > 0
                          && !string.IsNullOrWhiteSpace(DatasetId);

    public bool HasResults => ResultColumns.Count > 0 && ExecutionRows is IEnumerable;

    public string ResultSummary => !HasResults
        ? "Todavia no has ejecutado ninguna plantilla."
        : $"{ResultColumns.Count} columnas visibles, {RuleResults.Count} resultado(s) de reglas y {ExecutionWarnings.Count} advertencia(s).";

    public CustomReportRunnerViewModel(
        DynamicReportService dynamicReportService,
        PowerBiSelectionState selectionState,
        PowerBiDefaultSelectionService defaultSelectionService,
        ILogger<CustomReportRunnerViewModel> logger)
    {
        _dynamicReportService = dynamicReportService;
        _selectionState = selectionState;
        _defaultSelectionService = defaultSelectionService;
        _logger = logger;

        var now = DateTime.Now;
        SelectedYear = now.Year;

        for (var month = 1; month <= 12; month++)
        {
            Months.Add(new MonthOption
            {
                Value = month,
                Name = new DateTime(now.Year, month, 1).ToString("MMMM")
            });
        }

        for (var year = now.Year - 3; year <= now.Year + 1; year++)
            Years.Add(year);

        SelectedMonth = Months[Math.Max(0, now.Month - 1)];

        SelectedVendors.CollectionChanged += (_, __) =>
        {
            OnPropertyChanged(nameof(CanRun));
        };
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        _initialized = true;
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        try
        {
            IsBusy = true;
            await _defaultSelectionService.EnsureSelectionStateAsync(resolveNames: false);
            RefreshDatasetSelection();
            await LoadTemplatesAsync();
            await LoadFiltersIfNeededAsync(forceReload: true);

            ShowInfo(
                "Plantillas listas",
                "Selecciona una plantilla, define periodo y ejecuta para consumir datos reales desde la API.",
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo recargar el runner de plantillas.");
            ShowInfo("Error", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ReloadFiltersAsync()
    {
        await LoadFiltersIfNeededAsync(forceReload: true);
    }

    [RelayCommand]
    private async Task RunTemplateAsync()
    {
        if (SelectedTemplate == null || SelectedMonth == null)
            return;

        try
        {
            IsBusy = true;
            ClearResults();

            var filters = BuildFilters();
            var selectedVendorCodes = SelectedVendors
                .Select(option => option.Value?.Display ?? option.DisplayName)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .ToList();

            var result = await _dynamicReportService.ExecuteTemplateAsync(
                SelectedTemplate,
                DatasetId,
                SelectedYear,
                SelectedMonth.Value,
                selectedVendorCodes,
                filters);

            Replace(ResultColumns, result.Columns);
            Replace(RuleResults, result.RuleResults.OrderByDescending(rule => rule.Succeeded).ThenBy(rule => rule.RuleName, StringComparer.OrdinalIgnoreCase));
            Replace(ExecutionWarnings, result.Warnings);
            Replace(SummaryItems, result.Summaries.Select(item => new ExecutionSummaryItem(item.Key, FormatSummaryValue(item.Value))));
            ExecutionRows = result.Rows;
            ExecutionPreview = TabularPreviewFormatter.FromExecutionRows(result.Rows, result.Columns);

            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(ResultSummary));

            ShowInfo(
                "Plantilla ejecutada",
                $"Se ejecutaron {result.Rows.Count} fila(s) usando la plantilla '{SelectedTemplate.Nombre}'.",
                result.Warnings.Count == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo ejecutar la plantilla {TemplateId}.", SelectedTemplate?.Id);
            ShowInfo("Error ejecutando plantilla", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SetSelectedVendors(List<FilterOption> selected)
    {
        SelectedVendors.Clear();
        foreach (var option in selected)
            SelectedVendors.Add(option);

        VendorSelectionText = SelectedVendors.Count switch
        {
            0 => "Todos los vendedores",
            1 => SelectedVendors[0].DisplayName,
            _ => $"{SelectedVendors.Count} vendedores seleccionados"
        };
    }

    public void RefreshDatasetSelection()
    {
        if (_selectionState.SelectedDataset == null)
        {
            DatasetName = string.Empty;
            DatasetId = string.Empty;
            ShowInfo(
                "Dataset no seleccionado",
                "Configura DefaultWorkspaceId y DefaultDatasetId en Settings para ejecutar plantillas.",
                InfoBarSeverity.Warning);
            return;
        }

        DatasetName = _selectionState.SelectedDataset.Name;
        DatasetId = _selectionState.SelectedDataset.Id;
        OnPropertyChanged(nameof(CanRun));
    }

    partial void OnSelectedTemplateChanged(ReportTemplate? value)
    {
        if (value == null)
        {
            TemplateSourceText = "Sin plantilla seleccionada";
            TemplateColumnsText = "Las columnas visibles apareceran aqui al elegir una plantilla.";
            TemplateRulesText = "Las reglas configuradas se mostraran al ejecutar.";
            return;
        }

        var source = string.IsNullOrWhiteSpace(value.ReporteOrigen) ? value.TipoReporte : value.ReporteOrigen;
        var columns = ParseColumnDesign(value.ColumnDesign);
        var preview = columns.Count == 0
            ? "Sin columnas configuradas."
            : string.Join(", ", columns.Take(5)) + (columns.Count > 5 ? "..." : string.Empty);

        TemplateSourceText = $"Source real: {source}";
        TemplateColumnsText = columns.Count == 0
            ? "La plantilla no tiene columnas visibles definidas."
            : $"{columns.Count} columna(s): {preview}";
        TemplateRulesText = DescribeTemplateRules(value.Ruth);

        OnPropertyChanged(nameof(CanRun));
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanRun));
    partial void OnSelectedMonthChanged(MonthOption? value) => OnPropertyChanged(nameof(CanRun));
    partial void OnSelectedYearChanged(int value) => OnPropertyChanged(nameof(CanRun));
    partial void OnDatasetIdChanged(string value) => OnPropertyChanged(nameof(CanRun));
    partial void OnSelectedVendorsCsvChanged(string value) => ApplySelectedVendorsFromCsv(value);

    private async Task LoadTemplatesAsync()
    {
        var templates = await _dynamicReportService.GetTemplatesAsync();
        Replace(Templates, templates);

        if (SelectedTemplate == null && Templates.Count > 0)
            SelectedTemplate = Templates[0];
        else if (SelectedTemplate != null)
            SelectedTemplate = Templates.FirstOrDefault(template => template.Id == SelectedTemplate.Id) ?? Templates.FirstOrDefault();
    }

    private async Task LoadFiltersIfNeededAsync(bool forceReload)
    {
        if (string.IsNullOrWhiteSpace(DatasetId))
            return;

        if (!forceReload && _filtersLoadedForDatasetId == DatasetId && VendorOptions.Count > 0)
            return;

        IsFiltersLoading = true;
        try
        {
            Replace(VendorOptions, await _dynamicReportService.GetVendorOptionsAsync(DatasetId));
            SetSelectedVendors(new List<FilterOption>());
            if (!string.IsNullOrWhiteSpace(SelectedVendorsCsv))
                ApplySelectedVendorsFromCsv(SelectedVendorsCsv);

            var groups = new List<FilterOption>
            {
                new() { DisplayName = "Todos", Value = null }
            };

            try
            {
                groups.AddRange(await _dynamicReportService.GetFilterOptionsAsync("Grupo", DatasetId));
                IsGroupAvailable = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudieron cargar grupos para el runner.");
                IsGroupAvailable = false;
            }

            Replace(GroupOptions, groups);
            SelectedGroup = GroupOptions.FirstOrDefault();
            _filtersLoadedForDatasetId = DatasetId;
        }
        finally
        {
            IsFiltersLoading = false;
        }
    }

    private List<ReportFilterDefinition> BuildFilters()
    {
        var filters = new List<ReportFilterDefinition>();
        if (IsGroupAvailable && SelectedGroup?.Value != null)
        {
            filters.Add(new ReportFilterDefinition
            {
                Key = "Grupo",
                DisplayName = "Grupo",
                Value = SelectedGroup.Value.Display
            });
        }

        return filters;
    }

    private void ClearResults()
    {
        ResultColumns.Clear();
        RuleResults.Clear();
        SummaryItems.Clear();
        ExecutionWarnings.Clear();
        ExecutionRows = Array.Empty<Dictionary<string, object?>>();
        ExecutionPreview = "Todavia no hay resultados.";
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(ResultSummary));
    }

    private void ApplySelectedVendorsFromCsv(string? value)
    {
        if (VendorOptions.Count == 0)
            return;

        var tokens = (value ?? string.Empty)
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.Split(" - ", 2, StringSplitOptions.TrimEntries)[0])
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selected = VendorOptions
            .Where(option => option.Value != null && tokens.Contains(option.Value.Display, StringComparer.OrdinalIgnoreCase))
            .ToList();

        SetSelectedVendors(selected);
    }

    private void ShowInfo(string title, string message, InfoBarSeverity severity)
    {
        StatusTitle = title;
        StatusMessage = message;
        StatusSeverity = severity;
        IsStatusOpen = true;
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
            collection.Add(item);
    }

    private static List<string> ParseColumnDesign(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new List<string>();

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();
    }

    private static string DescribeTemplateRules(IReadOnlyList<JsonElement> rules)
    {
        if (rules.Count == 0)
            return "Sin reglas guardadas.";

        var grouped = rules.FirstOrDefault(rule =>
            rule.ValueKind == JsonValueKind.Object
            && (rule.TryGetProperty("rules_general", out _) || rule.TryGetProperty("rules_tiered", out _)));

        if (grouped.ValueKind == JsonValueKind.Object)
        {
            var general = CountRuleArray(grouped, "rules_general");
            var tiered = CountRuleArray(grouped, "rules_tiered");
            var legacy = CountRuleArray(grouped, "rules_legacy");

            return $"Reglas guardadas: {general} general(es), {tiered} escalonada(s)"
                   + (legacy > 0 ? $", {legacy} legacy conservada(s)." : ".");
        }

        return $"{rules.Count} bloque(s) de reglas guardados para esta plantilla.";
    }

    private static int CountRuleArray(JsonElement node, string propertyName)
    {
        if (!node.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            return 0;

        return property.GetArrayLength();
    }

    private static string FormatSummaryValue(object? value)
        => value switch
        {
            null => "-",
            DateTime dateTime => dateTime.ToLocalTime().ToString("g"),
            decimal number => number.ToString("0.##"),
            double number => number.ToString("0.##"),
            float number => number.ToString("0.##"),
            _ => value.ToString() ?? "-"
        };
}

public sealed class ExecutionSummaryItem
{
    public ExecutionSummaryItem(string label, string value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }
    public string Value { get; }
}
