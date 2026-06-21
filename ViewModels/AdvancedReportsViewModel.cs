
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zenit.Infrastructure.Logging;
using Zenit.Models.CustomReports;
using Zenit.Services;

namespace Zenit.ViewModels;

public partial class AdvancedReportsViewModel : ObservableRecipient
{
    private readonly DynamicReportService _dynamicReportService;
    private readonly PowerBiDefaultSelectionService _defaultSelectionService;
    private readonly ReportTemplateRuleSchemaService _ruleSchemaService;
    private readonly ILogger<AdvancedReportsViewModel> _logger;
    private bool _initialized;
    private bool _suppressSourceReload;

    public ObservableCollection<ReportTemplate> Templates { get; } = new();
    public ObservableCollection<ReportTypeDefinition> ReportTypes { get; } = new();

    public ObservableCollection<ReportColumnDefinition> AvailableColumns { get; } = new();
    public ObservableCollection<ReportColumnDefinition> SelectedColumns { get; } = new();
    public ObservableCollection<ReportColumnDefinition> FilteredAvailableColumns { get; } = new();
    public ObservableCollection<ReportColumnDefinition> FilteredSelectedColumns { get; } = new();

    public ObservableCollection<TemplateGlobalRuleUiModel> GlobalRules { get; } = new();
    public ObservableCollection<TemplateScaleRuleUiModel> ScaleRules { get; } = new();
    public ObservableCollection<TemplateScaleTierUiModel> DraftScaleTiers { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;

    [ObservableProperty] private string reportName = "Nueva plantilla";
    [ObservableProperty] private string source = string.Empty;
    [ObservableProperty] private string columnDesign = string.Empty;
    [ObservableProperty] private string activeDatasetId = string.Empty;
    [ObservableProperty] private bool isDatasetConfigured;

    [ObservableProperty] private string availableColumnSearch = string.Empty;
    [ObservableProperty] private string selectedColumnSearch = string.Empty;

    [ObservableProperty] private ReportTemplate? selectedTemplate;
    [ObservableProperty] private ReportTypeDefinition? selectedReportType;
    [ObservableProperty] private ReportColumnDefinition? selectedAvailableColumn;
    [ObservableProperty] private ReportColumnDefinition? selectedColumn;

    [ObservableProperty] private TemplateGlobalRuleUiModel? selectedGlobalRule;
    [ObservableProperty] private string globalRuleLine = string.Empty;
    [ObservableProperty] private string globalRuleScope = "descripcion";
    [ObservableProperty] private string globalRuleCurrency = "NIO";
    [ObservableProperty] private string globalRuleRoutes = "*";
    [ObservableProperty] private string globalRuleMetric = "volume";
    [ObservableProperty] private string globalRuleOperator = ">=";
    [ObservableProperty] private string globalRuleValue = "100";
    [ObservableProperty] private string globalRuleRewardAmount = string.Empty;
    [ObservableProperty] private string globalRuleOverrideRoutes = string.Empty;
    [ObservableProperty] private string globalRuleOverrideRewardAmount = string.Empty;
    [ObservableProperty] private string globalRuleSuccessColor = "green";
    [ObservableProperty] private string globalRuleSuccessStatus = "cumple";
    [ObservableProperty] private string globalRuleFailColor = "red";
    [ObservableProperty] private string globalRuleFailStatus = "no_cumple";

    [ObservableProperty] private TemplateScaleRuleUiModel? selectedScaleRule;
    [ObservableProperty] private TemplateScaleTierUiModel? selectedDraftScaleTier;
    [ObservableProperty] private string scaleRuleLine = string.Empty;
    [ObservableProperty] private string scaleRuleCurrency = "NIO";
    [ObservableProperty] private string scaleRuleMetric = "volume";
    [ObservableProperty] private string scaleTierMin = string.Empty;
    [ObservableProperty] private string scaleTierMax = string.Empty;
    [ObservableProperty] private string scaleTierDefaultReward = "0";
    [ObservableProperty] private string scaleTierOverrides = string.Empty;

    public AdvancedReportsViewModel(
        DynamicReportService dynamicReportService,
        PowerBiDefaultSelectionService defaultSelectionService,
        ReportTemplateRuleSchemaService ruleSchemaService,
        ILogger<AdvancedReportsViewModel> logger)
    {
        _dynamicReportService = dynamicReportService;
        _defaultSelectionService = defaultSelectionService;
        _ruleSchemaService = ruleSchemaService;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        _initialized = true;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            IsBusy = true;
            await RefreshMetadataAsync();
            await LoadTemplatesAsync();
            if (!string.IsNullOrWhiteSpace(Source))
                await ReloadColumnsAsync(true);
            StatusMessage = "Plantillas y metadata actualizadas.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo refrescar Custom Report.");
            StatusMessage = $"No se pudo refrescar: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task NewTemplateAsync()
    {
        SelectedTemplate = null;
        ReportName = "Nueva plantilla";
        Source = SelectedReportType?.Key ?? string.Empty;
        SelectedColumns.Clear();
        AvailableColumns.Clear();
        ColumnDesign = string.Empty;
        ClearRules();
        RefreshColumnViews();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task LoadTemplateAsync()
    {
        if (SelectedTemplate == null)
        {
            StatusMessage = "Selecciona una plantilla para cargar.";
            return;
        }

        try
        {
            IsBusy = true;
            await ApplyTemplateAsync(SelectedTemplate);
            StatusMessage = $"Plantilla '{SelectedTemplate.Nombre}' cargada.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo cargar plantilla.");
            StatusMessage = $"No se pudo cargar: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveTemplateAsync()
    {
        if (string.IsNullOrWhiteSpace(ReportName))
        {
            StatusMessage = "Name es obligatorio.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Source))
        {
            StatusMessage = "Source es obligatorio.";
            return;
        }

        try
        {
            IsBusy = true;
            var template = BuildTemplate();
            var saved = await _dynamicReportService.SaveTemplateAsync(template);
            await LoadTemplatesAsync();
            SelectedTemplate = Templates.FirstOrDefault(t => t.Id == saved.Id);
            StatusMessage = $"Plantilla '{saved.Nombre}' guardada.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo guardar plantilla.");
            StatusMessage = $"No se pudo guardar: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DuplicateTemplateAsync()
    {
        if (SelectedTemplate == null)
        {
            StatusMessage = "Selecciona una plantilla para duplicar.";
            return;
        }

        try
        {
            IsBusy = true;
            var copy = await _dynamicReportService.DuplicateTemplateAsync(SelectedTemplate.Id);
            await LoadTemplatesAsync();
            SelectedTemplate = Templates.FirstOrDefault(t => t.Id == copy.Id);
            StatusMessage = $"Plantilla duplicada: {copy.Nombre}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo duplicar plantilla.");
            StatusMessage = $"No se pudo duplicar: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteTemplateAsync()
    {
        if (SelectedTemplate == null)
        {
            StatusMessage = "Selecciona una plantilla para eliminar.";
            return;
        }

        try
        {
            IsBusy = true;
            var deletedName = SelectedTemplate.Nombre;
            await _dynamicReportService.DeleteTemplateAsync(SelectedTemplate.Id);
            await LoadTemplatesAsync();
            await NewTemplateAsync();
            StatusMessage = $"Plantilla '{deletedName}' eliminada.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo eliminar plantilla.");
            StatusMessage = $"No se pudo eliminar: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
    [RelayCommand]
    private void AddColumn()
    {
        if (SelectedAvailableColumn == null)
            return;
        MoveToSelected(new[] { SelectedAvailableColumn });
        SelectedAvailableColumn = null;
    }

    [RelayCommand]
    private void RemoveColumn()
    {
        if (SelectedColumn == null)
            return;
        MoveToAvailable(new[] { SelectedColumn });
        SelectedColumn = null;
    }

    [RelayCommand] private void AddVisibleColumns() => MoveToSelected(FilteredAvailableColumns.ToList());
    [RelayCommand] private void RemoveVisibleColumns() => MoveToAvailable(FilteredSelectedColumns.ToList());
    [RelayCommand] private void ClearSelectedColumns() => MoveToAvailable(SelectedColumns.ToList());

    [RelayCommand]
    private void MoveColumnUp() => MoveSelectedColumn(-1);

    [RelayCommand]
    private void MoveColumnDown() => MoveSelectedColumn(1);

    [RelayCommand]
    private void MoveColumnTop()
    {
        if (SelectedColumn == null)
            return;
        var idx = SelectedColumns.IndexOf(SelectedColumn);
        if (idx > 0)
        {
            SelectedColumns.Move(idx, 0);
            NormalizeSelectedColumns();
        }
    }

    [RelayCommand]
    private void MoveColumnBottom()
    {
        if (SelectedColumn == null)
            return;
        var idx = SelectedColumns.IndexOf(SelectedColumn);
        if (idx >= 0 && idx < SelectedColumns.Count - 1)
        {
            SelectedColumns.Move(idx, SelectedColumns.Count - 1);
            NormalizeSelectedColumns();
        }
    }

    [RelayCommand]
    private void SaveGlobalRule()
    {
        if (string.IsNullOrWhiteSpace(GlobalRuleLine) || string.IsNullOrWhiteSpace(GlobalRuleMetric) || !TryParseDecimal(GlobalRuleValue, out var threshold))
        {
            StatusMessage = "Completa line, metric y value para la regla global.";
            return;
        }

        var target = SelectedGlobalRule ?? new TemplateGlobalRuleUiModel();
        target.Line = GlobalRuleLine.Trim();
        target.Scope = GlobalRuleScope.Trim();
        target.Currency = GlobalRuleCurrency.Trim();
        target.RoutesText = string.IsNullOrWhiteSpace(GlobalRuleRoutes) ? "*" : GlobalRuleRoutes.Trim();
        target.Metric = GlobalRuleMetric.Trim();
        target.Operator = string.IsNullOrWhiteSpace(GlobalRuleOperator) ? ">=" : GlobalRuleOperator.Trim();
        target.ThresholdValue = threshold;
        target.RewardAmount = TryParseDecimal(GlobalRuleRewardAmount, out var reward) ? reward : null;
        target.OverrideRoutesText = GlobalRuleOverrideRoutes.Trim();
        target.OverrideRewardAmount = TryParseDecimal(GlobalRuleOverrideRewardAmount, out var overReward) ? overReward : null;
        target.SuccessColor = GlobalRuleSuccessColor.Trim();
        target.SuccessStatus = GlobalRuleSuccessStatus.Trim();
        target.FailColor = GlobalRuleFailColor.Trim();
        target.FailStatus = GlobalRuleFailStatus.Trim();

        if (SelectedGlobalRule == null)
            GlobalRules.Add(target);

        SelectedGlobalRule = target;
        StatusMessage = "Regla global guardada.";
    }

    [RelayCommand]
    private void DeleteGlobalRule()
    {
        if (SelectedGlobalRule == null)
            return;
        GlobalRules.Remove(SelectedGlobalRule);
        ClearGlobalRuleEditor();
        StatusMessage = "Regla global eliminada.";
    }

    [RelayCommand]
    private void ClearGlobalRuleEditor()
    {
        SelectedGlobalRule = null;
        GlobalRuleLine = string.Empty;
        GlobalRuleScope = "descripcion";
        GlobalRuleCurrency = "NIO";
        GlobalRuleRoutes = "*";
        GlobalRuleMetric = "volume";
        GlobalRuleOperator = ">=";
        GlobalRuleValue = "100";
        GlobalRuleRewardAmount = string.Empty;
        GlobalRuleOverrideRoutes = string.Empty;
        GlobalRuleOverrideRewardAmount = string.Empty;
        GlobalRuleSuccessColor = "green";
        GlobalRuleSuccessStatus = "cumple";
        GlobalRuleFailColor = "red";
        GlobalRuleFailStatus = "no_cumple";
    }

    [RelayCommand]
    private void AddScaleTier()
    {
        if (!TryParseDecimal(ScaleTierMin, out var min) || !TryParseDecimal(ScaleTierDefaultReward, out var defaultReward))
        {
            StatusMessage = "Min y Default Reward son obligatorios en tier.";
            return;
        }

        decimal? max = null;
        if (!string.IsNullOrWhiteSpace(ScaleTierMax))
        {
            if (!TryParseDecimal(ScaleTierMax, out var maxParsed))
            {
                StatusMessage = "Max de tier invalido.";
                return;
            }
            max = maxParsed;
        }

        var overrides = ParseOverrides(ScaleTierOverrides);
        if (overrides == null)
        {
            StatusMessage = "Overrides invalido. Usa formato 13:300,14:400";
            return;
        }

        DraftScaleTiers.Add(new TemplateScaleTierUiModel
        {
            Min = min,
            Max = max,
            DefaultReward = defaultReward,
            Overrides = new ObservableCollection<TemplateScaleTierOverrideUiModel>(overrides)
        });

        ClearScaleTierEditor();
    }

    [RelayCommand]
    private void RemoveScaleTier()
    {
        if (SelectedDraftScaleTier == null)
            return;
        DraftScaleTiers.Remove(SelectedDraftScaleTier);
        ClearScaleTierEditor();
    }

    [RelayCommand]
    private void ClearScaleTierEditor()
    {
        SelectedDraftScaleTier = null;
        ScaleTierMin = string.Empty;
        ScaleTierMax = string.Empty;
        ScaleTierDefaultReward = "0";
        ScaleTierOverrides = string.Empty;
    }

    [RelayCommand]
    private void SaveScaleRule()
    {
        if (string.IsNullOrWhiteSpace(ScaleRuleLine) || string.IsNullOrWhiteSpace(ScaleRuleMetric) || DraftScaleTiers.Count == 0)
        {
            StatusMessage = "Line, metric y al menos un tier son requeridos para regla escalonada.";
            return;
        }

        var target = SelectedScaleRule ?? new TemplateScaleRuleUiModel();
        target.Line = ScaleRuleLine.Trim();
        target.Currency = ScaleRuleCurrency.Trim();
        target.Metric = ScaleRuleMetric.Trim();
        target.Tiers = new ObservableCollection<TemplateScaleTierUiModel>(DraftScaleTiers.Select(CloneTier));

        if (SelectedScaleRule == null)
            ScaleRules.Add(target);

        SelectedScaleRule = target;
        StatusMessage = "Regla escalonada guardada.";
    }

    [RelayCommand]
    private void DeleteScaleRule()
    {
        if (SelectedScaleRule == null)
            return;
        ScaleRules.Remove(SelectedScaleRule);
        ClearScaleRuleEditor();
        StatusMessage = "Regla escalonada eliminada.";
    }

    [RelayCommand]
    private void ClearScaleRuleEditor()
    {
        SelectedScaleRule = null;
        ScaleRuleLine = string.Empty;
        ScaleRuleCurrency = "NIO";
        ScaleRuleMetric = "volume";
        DraftScaleTiers.Clear();
        ClearScaleTierEditor();
    }
    partial void OnSelectedGlobalRuleChanged(TemplateGlobalRuleUiModel? value)
    {
        if (value == null)
            return;
        GlobalRuleLine = value.Line;
        GlobalRuleScope = value.Scope;
        GlobalRuleCurrency = value.Currency;
        GlobalRuleRoutes = value.RoutesText;
        GlobalRuleMetric = value.Metric;
        GlobalRuleOperator = value.Operator;
        GlobalRuleValue = value.ThresholdValue.ToString(CultureInfo.InvariantCulture);
        GlobalRuleRewardAmount = value.RewardAmount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        GlobalRuleOverrideRoutes = value.OverrideRoutesText;
        GlobalRuleOverrideRewardAmount = value.OverrideRewardAmount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        GlobalRuleSuccessColor = value.SuccessColor;
        GlobalRuleSuccessStatus = value.SuccessStatus;
        GlobalRuleFailColor = value.FailColor;
        GlobalRuleFailStatus = value.FailStatus;
    }

    partial void OnSelectedScaleRuleChanged(TemplateScaleRuleUiModel? value)
    {
        if (value == null)
            return;
        ScaleRuleLine = value.Line;
        ScaleRuleCurrency = value.Currency;
        ScaleRuleMetric = value.Metric;
        DraftScaleTiers.Clear();
        foreach (var tier in value.Tiers.Select(CloneTier))
            DraftScaleTiers.Add(tier);
        ClearScaleTierEditor();
    }

    partial void OnSelectedDraftScaleTierChanged(TemplateScaleTierUiModel? value)
    {
        if (value == null)
            return;
        ScaleTierMin = value.Min.ToString(CultureInfo.InvariantCulture);
        ScaleTierMax = value.Max?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        ScaleTierDefaultReward = value.DefaultReward.ToString(CultureInfo.InvariantCulture);
        ScaleTierOverrides = string.Join(",", value.Overrides.Select(o => $"{o.RoutesText}:{o.Amount.ToString(CultureInfo.InvariantCulture)}"));
    }

    partial void OnSelectedReportTypeChanged(ReportTypeDefinition? value)
    {
        if (value != null)
            Source = value.Key;
    }

    partial void OnSourceChanged(string value)
    {
        if (_suppressSourceReload)
            return;
        _ = ReloadBuilderMetadataSafeAsync(true);
    }

    partial void OnAvailableColumnSearchChanged(string value) => RefreshBuilderColumnViews();
    partial void OnSelectedColumnSearchChanged(string value) => RefreshBuilderColumnViews();

    private async Task RefreshMetadataAsync()
    {
        var selection = await _defaultSelectionService.EnsureSelectionStateAsync(resolveNames: false);
        ActiveDatasetId = selection.DatasetId;
        IsDatasetConfigured = !string.IsNullOrWhiteSpace(ActiveDatasetId);

        ReportTypes.Clear();
        if (!IsDatasetConfigured)
        {
            StatusMessage = "Dataset no configurado. Puedes escribir Source manualmente.";
            return;
        }

        foreach (var reportType in await _dynamicReportService.GetReportTypesAsync(ActiveDatasetId))
            ReportTypes.Add(reportType);

        if (string.IsNullOrWhiteSpace(Source) && ReportTypes.Count > 0)
            Source = ReportTypes[0].Key;
    }

    private async Task LoadTemplatesAsync()
    {
        Templates.Clear();
        foreach (var template in await _dynamicReportService.GetTemplatesAsync())
            Templates.Add(template);
    }

    private async Task ApplyTemplateAsync(ReportTemplate template)
    {
        _suppressSourceReload = true;
        try
        {
            ReportName = template.Nombre;
            Source = string.IsNullOrWhiteSpace(template.ReporteOrigen) ? template.TipoReporte : template.ReporteOrigen;
        }
        finally
        {
            _suppressSourceReload = false;
        }

        await ReloadColumnsAsync(false);

        var selectedKeys = ParseColumnDesign(template.ColumnDesign);
        if (selectedKeys.Count == 0 && template.Columnas.Count > 0)
            selectedKeys = template.Columnas.Select(c => Normalize(c.SourceField)).Where(s => s.Length > 0).ToList();

        var catalog = AvailableColumns.ToList();
        AvailableColumns.Clear();
        SelectedColumns.Clear();

        var set = selectedKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var col in catalog)
        {
            var sf = Normalize(col.SourceField);
            var key = Normalize(col.Key);
            var dn = Normalize(col.DisplayName);
            if (set.Contains(sf) || set.Contains(key) || set.Contains(dn))
                SelectedColumns.Add(CloneColumn(col));
            else
                AvailableColumns.Add(CloneColumn(col));
        }

        NormalizeSelectedColumns();
        LoadRulesFromRuth(template.Ruth);
    }

    private async Task ReloadColumnsSafeAsync(bool preserveSelection)
    {
        try
        {
            await ReloadColumnsAsync(preserveSelection);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudieron cargar columnas.");
            StatusMessage = $"No se pudieron cargar columnas: {ex.Message}";
        }
    }

    private async Task ReloadColumnsAsync(bool preserveSelection)
    {
        if (!IsDatasetConfigured || string.IsNullOrWhiteSpace(Source))
        {
            AvailableColumns.Clear();
            if (!preserveSelection)
                SelectedColumns.Clear();
            NormalizeSelectedColumns();
            return;
        }

        var keep = preserveSelection
            ? SelectedColumns.Select(c => Normalize(c.SourceField)).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var catalog = await _dynamicReportService.GetColumnCatalogAsync(ActiveDatasetId, Source, DateTime.Now.Year, DateTime.Now.Month, vendedores: null);

        AvailableColumns.Clear();
        SelectedColumns.Clear();

        foreach (var col in catalog.OrderBy(c => c.Order))
        {
            var clone = CloneColumn(col);
            if (keep.Contains(Normalize(clone.SourceField)))
                SelectedColumns.Add(clone);
            else
                AvailableColumns.Add(clone);
        }

        if (SelectedColumns.Count == 0)
        {
            foreach (var col in AvailableColumns.Take(Math.Min(6, AvailableColumns.Count)).ToList())
            {
                SelectedColumns.Add(CloneColumn(col));
                AvailableColumns.Remove(col);
            }
        }

        NormalizeSelectedColumns();
    }
    private void MoveToSelected(IEnumerable<ReportColumnDefinition> columns)
    {
        var next = SelectedColumns.Count == 0 ? 0 : SelectedColumns.Max(c => c.Order) + 1;
        foreach (var col in columns.ToList())
        {
            if (!AvailableColumns.Contains(col))
                continue;
            var clone = CloneColumn(col);
            clone.Order = next++;
            SelectedColumns.Add(clone);
            AvailableColumns.Remove(col);
        }
        NormalizeSelectedColumns();
    }

    private void MoveToAvailable(IEnumerable<ReportColumnDefinition> columns)
    {
        foreach (var col in columns.ToList())
        {
            if (!SelectedColumns.Contains(col))
                continue;
            AvailableColumns.Add(CloneColumn(col));
            SelectedColumns.Remove(col);
        }
        NormalizeSelectedColumns();
    }

    private void MoveSelectedColumn(int delta)
    {
        if (SelectedColumn == null)
            return;
        var idx = SelectedColumns.IndexOf(SelectedColumn);
        var target = idx + delta;
        if (idx < 0 || target < 0 || target >= SelectedColumns.Count)
            return;
        SelectedColumns.Move(idx, target);
        NormalizeSelectedColumns();
    }

    private void NormalizeSelectedColumns()
    {
        for (var i = 0; i < SelectedColumns.Count; i++)
            SelectedColumns[i].Order = i;

        ColumnDesign = string.Join(",", SelectedColumns.OrderBy(c => c.Order).Select(c => string.IsNullOrWhiteSpace(c.SourceField) ? c.Key : c.SourceField).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
        RefreshColumnViews();
    }

    private void RefreshColumnViews()
    {
        Replace(FilteredAvailableColumns, FilterColumns(AvailableColumns, AvailableColumnSearch));
        Replace(FilteredSelectedColumns, FilterColumns(SelectedColumns, SelectedColumnSearch));
    }

    private static IEnumerable<ReportColumnDefinition> FilterColumns(IEnumerable<ReportColumnDefinition> source, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return source;
        var term = search.Trim();
        return source.Where(c =>
            (c.DisplayName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (c.SourceField?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (c.Key?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    private ReportTemplate BuildTemplate()
    {
        var src = Source.Trim();
        return new ReportTemplate
        {
            Id = SelectedTemplate?.Id ?? Guid.NewGuid(),
            Nombre = ReportName.Trim(),
            TipoReporte = src,
            ReporteOrigen = src,
            ColumnDesign = ColumnDesign,
            Columnas = SelectedColumns.OrderBy(c => c.Order).Select(CloneColumn).ToList(),
            Ruth = BuildRuthArray()
        };
    }

    private List<JsonElement> BuildRuthArray()
    {
        var rules = new List<JsonElement>();

        foreach (var g in GlobalRules)
        {
            var node = new JsonObject
            {
                ["type"] = "rule",
                ["line"] = g.Line,
                ["scope"] = g.Scope,
                ["currency"] = g.Currency,
                ["routes"] = BuildRoutes(g.RoutesText),
                ["conditions"] = new JsonArray { new JsonObject { ["metric"] = g.Metric, ["operator"] = g.Operator, ["value"] = g.ThresholdValue } },
                ["visual"] = new JsonObject
                {
                    ["on_success"] = new JsonObject { ["color"] = g.SuccessColor, ["status"] = g.SuccessStatus },
                    ["on_fail"] = new JsonObject { ["color"] = g.FailColor, ["status"] = g.FailStatus }
                }
            };

            if (g.RewardAmount.HasValue)
                node["reward"] = new JsonObject { ["amount"] = g.RewardAmount.Value };

            if (!string.IsNullOrWhiteSpace(g.OverrideRoutesText) || g.OverrideRewardAmount.HasValue)
            {
                var over = new JsonObject { ["routes"] = BuildRoutes(g.OverrideRoutesText) };
                if (g.OverrideRewardAmount.HasValue)
                    over["reward"] = new JsonObject { ["amount"] = g.OverrideRewardAmount.Value };
                node["overrides"] = new JsonArray { over };
            }

            rules.Add(ToElement(node));
        }

        foreach (var s in ScaleRules)
        {
            var tiers = new JsonArray();
            foreach (var t in s.Tiers)
            {
                var tier = new JsonObject
                {
                    ["min"] = t.Min,
                    ["max"] = t.Max.HasValue ? JsonValue.Create(t.Max.Value) : null,
                    ["default_reward"] = t.DefaultReward
                };

                if (t.Overrides.Count > 0)
                {
                    var overrides = new JsonArray();
                    foreach (var o in t.Overrides)
                        overrides.Add(new JsonObject { ["routes"] = BuildRoutes(o.RoutesText), ["amount"] = o.Amount });
                    tier["overrides"] = overrides;
                }

                tiers.Add(tier);
            }

            var node = new JsonObject
            {
                ["type"] = "escala rule",
                ["line"] = s.Line,
                ["currency"] = s.Currency,
                ["metric"] = s.Metric,
                ["tiers"] = tiers
            };

            rules.Add(ToElement(node));
        }

        return rules;
    }

    private void LoadRulesFromRuth(IEnumerable<JsonElement> ruth)
    {
        ClearRules();
        foreach (var rule in ruth)
        {
            if (rule.ValueKind != JsonValueKind.Object)
                continue;
            var type = GetString(rule, "type");
            if (type.Contains("escala", StringComparison.OrdinalIgnoreCase))
                ParseScale(rule);
            else
                ParseGlobal(rule);
        }
    }

    private void ParseGlobal(JsonElement rule)
    {
        var model = new TemplateGlobalRuleUiModel
        {
            Line = GetString(rule, "line"),
            Scope = GetString(rule, "scope"),
            Currency = GetString(rule, "currency"),
            RoutesText = GetRoutes(rule, "routes", "*"),
            Metric = "volume",
            Operator = ">="
        };

        if (rule.TryGetProperty("conditions", out var conds) && conds.ValueKind == JsonValueKind.Array && conds.GetArrayLength() > 0)
        {
            var c = conds[0];
            model.Metric = GetString(c, "metric");
            model.Operator = GetString(c, "operator");
            model.ThresholdValue = GetDecimal(c, "value");
        }

        if (rule.TryGetProperty("reward", out var reward) && reward.ValueKind == JsonValueKind.Object)
            model.RewardAmount = TryGetDecimal(reward, "amount", out var amount) ? amount : null;

        if (rule.TryGetProperty("overrides", out var overs) && overs.ValueKind == JsonValueKind.Array && overs.GetArrayLength() > 0)
        {
            var ov = overs[0];
            model.OverrideRoutesText = GetRoutes(ov, "routes", string.Empty);
            if (ov.TryGetProperty("reward", out var ovReward) && ovReward.ValueKind == JsonValueKind.Object)
                model.OverrideRewardAmount = TryGetDecimal(ovReward, "amount", out var ovAmount) ? ovAmount : null;
        }

        if (rule.TryGetProperty("visual", out var visual) && visual.ValueKind == JsonValueKind.Object)
        {
            if (visual.TryGetProperty("on_success", out var ok))
            {
                model.SuccessColor = GetString(ok, "color");
                model.SuccessStatus = GetString(ok, "status");
            }
            if (visual.TryGetProperty("on_fail", out var fail))
            {
                model.FailColor = GetString(fail, "color");
                model.FailStatus = GetString(fail, "status");
            }
        }

        if (!string.IsNullOrWhiteSpace(model.Line))
            GlobalRules.Add(model);
    }

    private void ParseScale(JsonElement rule)
    {
        var model = new TemplateScaleRuleUiModel
        {
            Line = GetString(rule, "line"),
            Currency = GetString(rule, "currency"),
            Metric = GetString(rule, "metric"),
            Tiers = new ObservableCollection<TemplateScaleTierUiModel>()
        };
        if (rule.TryGetProperty("tiers", out var tiers) && tiers.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in tiers.EnumerateArray())
            {
                if (t.ValueKind != JsonValueKind.Object)
                    continue;
                var tier = new TemplateScaleTierUiModel
                {
                    Min = GetDecimal(t, "min"),
                    Max = t.TryGetProperty("max", out var max) && max.ValueKind != JsonValueKind.Null ? GetDecimal(t, "max") : null,
                    DefaultReward = GetDecimal(t, "default_reward"),
                    Overrides = new ObservableCollection<TemplateScaleTierOverrideUiModel>()
                };

                if (t.TryGetProperty("overrides", out var overArr) && overArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var o in overArr.EnumerateArray())
                    {
                        if (o.ValueKind != JsonValueKind.Object)
                            continue;
                        tier.Overrides.Add(new TemplateScaleTierOverrideUiModel
                        {
                            RoutesText = GetRoutes(o, "routes", string.Empty),
                            Amount = GetDecimal(o, "amount")
                        });
                    }
                }

                model.Tiers.Add(tier);
            }
        }

        if (!string.IsNullOrWhiteSpace(model.Line))
            ScaleRules.Add(model);
    }

    private void ClearRules()
    {
        GlobalRules.Clear();
        ScaleRules.Clear();
        ClearGlobalRuleEditor();
        ClearScaleRuleEditor();
    }

    private static List<TemplateScaleTierOverrideUiModel>? ParseOverrides(string text)
    {
        var list = new List<TemplateScaleTierOverrideUiModel>();
        if (string.IsNullOrWhiteSpace(text))
            return list;

        foreach (var item in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = item.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !TryParseDecimal(parts[1], out var amount))
                return null;
            list.Add(new TemplateScaleTierOverrideUiModel { RoutesText = parts[0], Amount = amount });
        }

        return list;
    }

    private static JsonArray BuildRoutes(string text)
    {
        var routes = new JsonArray();
        var parts = (string.IsNullOrWhiteSpace(text) ? "*" : text).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (int.TryParse(part, out var route))
                routes.Add(route);
            else
                routes.Add(part);
        }
        if (routes.Count == 0)
            routes.Add("*");
        return routes;
    }

    private static string GetRoutes(JsonElement obj, string property, string fallback)
    {
        if (!obj.TryGetProperty(property, out var routes) || routes.ValueKind != JsonValueKind.Array)
            return fallback;

        var values = routes.EnumerateArray().Select(r => r.ValueKind switch
        {
            JsonValueKind.Number => r.GetRawText(),
            JsonValueKind.String => r.GetString() ?? string.Empty,
            _ => string.Empty
        }).Where(v => !string.IsNullOrWhiteSpace(v));

        var text = string.Join(",", values);
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    private static JsonElement ToElement(JsonNode node)
    {
        using var doc = JsonDocument.Parse(node.ToJsonString());
        return doc.RootElement.Clone();
    }

    private static List<string> ParseColumnDesign(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new List<string>();

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(v => v.Length > 0)
            .ToList();
    }

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
            collection.Add(item);
    }

    private static ReportColumnDefinition CloneColumn(ReportColumnDefinition c)
    {
        return new ReportColumnDefinition
        {
            Key = c.Key,
            DisplayName = c.DisplayName,
            SourceTable = c.SourceTable,
            SourceField = c.SourceField,
            DataType = c.DataType,
            SourceType = c.SourceType,
            IsMeasure = c.IsMeasure,
            IsDimension = c.IsDimension,
            IsCalculated = c.IsCalculated,
            Order = c.Order,
            IsVisible = c.IsVisible,
            FormatString = c.FormatString,
            DefaultFormat = c.DefaultFormat,
            AllowSorting = c.AllowSorting,
            AllowFiltering = c.AllowFiltering,
            AllowRules = c.AllowRules,
            VisibleInColumnSelector = c.VisibleInColumnSelector,
            VisibleInAdvancedMode = c.VisibleInAdvancedMode,
            CatalogCategory = c.CatalogCategory,
            CatalogCanonicalKey = c.CatalogCanonicalKey
        };
    }

    private static TemplateScaleTierUiModel CloneTier(TemplateScaleTierUiModel t)
    {
        return new TemplateScaleTierUiModel
        {
            Min = t.Min,
            Max = t.Max,
            DefaultReward = t.DefaultReward,
            Overrides = new ObservableCollection<TemplateScaleTierOverrideUiModel>(
                t.Overrides.Select(o => new TemplateScaleTierOverrideUiModel
                {
                    RoutesText = o.RoutesText,
                    Amount = o.Amount
                }))
        };
    }

    private static bool TryParseDecimal(string? input, out decimal value)
    {
        if (decimal.TryParse(input, NumberStyles.Any, CultureInfo.CurrentCulture, out value))
            return true;
        return decimal.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private static string GetString(JsonElement obj, string property)
    {
        if (!obj.TryGetProperty(property, out var p))
            return string.Empty;
        return p.ValueKind switch
        {
            JsonValueKind.String => p.GetString() ?? string.Empty,
            JsonValueKind.Number => p.GetRawText(),
            _ => string.Empty
        };
    }

    private static decimal GetDecimal(JsonElement obj, string property)
        => TryGetDecimal(obj, property, out var value) ? value : 0m;

    private static bool TryGetDecimal(JsonElement obj, string property, out decimal value)
    {
        value = 0m;
        if (!obj.TryGetProperty(property, out var p))
            return false;
        if (p.ValueKind == JsonValueKind.Number)
            return p.TryGetDecimal(out value);
        if (p.ValueKind == JsonValueKind.String)
            return TryParseDecimal(p.GetString(), out value);
        return false;
    }
}
