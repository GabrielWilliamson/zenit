using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zenit.Infrastructure.Logging;
using Zenit.Models.CustomReports;

namespace Zenit.ViewModels;

public partial class AdvancedReportsViewModel
{
    private bool _builderInitialized;
    private bool _perDescriptionLinesSummaryHooked;
    private IReadOnlyList<ReportFieldDefinition> _builderFieldCatalog = Array.Empty<ReportFieldDefinition>();
    private List<JsonElement> _builderPreservedLegacyRuleNodes = new();

    public ObservableCollection<ReportColumnPickerItem> AvailableTemplateColumns { get; } = new();
    public ObservableCollection<ReportColumnPickerItem> SelectedTemplateColumns { get; } = new();
    public ObservableCollection<ReportColumnPickerItem> FilteredAvailableTemplateColumns { get; } = new();
    public ObservableCollection<ReportColumnPickerItem> FilteredSelectedTemplateColumns { get; } = new();

    public ObservableCollection<ReportFieldOption> RuleDimensionChoices { get; } = new();
    public ObservableCollection<ReportFieldOption> RuleMetricChoices { get; } = new();
    public ObservableCollection<BuilderOption> ColumnViewModeChoices { get; } = new();
    public ObservableCollection<BuilderOption> RuleTypePresetChoices { get; } = new();
    public ObservableCollection<BuilderOption> RuleOperatorChoices { get; } = new();
    public ObservableCollection<BuilderOption> RuleEvaluationChoices { get; } = new();
    public ObservableCollection<BuilderOption> RuleActionChoices { get; } = new();

    public ObservableCollection<GuidedRuleUiModel> ConfiguredRules { get; } = new();
    public ObservableCollection<GuidedRuleUiModel> ConfiguredGeneralRules { get; } = new();
    public ObservableCollection<GuidedRuleUiModel> ConfiguredTieredRules { get; } = new();
    public ObservableCollection<GuidedRuleTierUiModel> RuleEditorTiers { get; } = new();
    public ObservableCollection<DescriptionAmountRuleLineUiModel> PerDescriptionRuleLines { get; } = new();
    public ObservableCollection<MultiDescriptionRouteExceptionUiModel> MultiDescriptionRouteExceptions { get; } = new();
    public ObservableCollection<LegacyRuleUiModel> PreservedLegacyRules { get; } = new();

    [ObservableProperty] private ReportColumnPickerItem? selectedAvailableTemplateColumn;
    [ObservableProperty] private ReportColumnPickerItem? selectedTemplateColumn;
    [ObservableProperty] private GuidedRuleUiModel? selectedConfiguredRule;
    [ObservableProperty] private GuidedRuleTierUiModel? selectedRuleEditorTier;
    [ObservableProperty] private string columnViewMode = "all";
    [ObservableProperty] private string ruleEditorTypePreset = "simple";

    [ObservableProperty] private string ruleEditorName = string.Empty;
    [ObservableProperty] private string ruleEditorNameValidationMessage = string.Empty;
    [ObservableProperty] private string ruleEditorMetricValidationMessage = string.Empty;
    [ObservableProperty] private string ruleEditorConditionValidationMessage = string.Empty;
    [ObservableProperty] private string ruleEditorRewardValidationMessage = string.Empty;
    [ObservableProperty] private string perDescriptionDuplicateValidationMessage = string.Empty;
    [ObservableProperty] private string ruleEditorDimension = string.Empty;
    [ObservableProperty] private string ruleEditorMetric = string.Empty;
    [ObservableProperty] private string ruleEditorOperator = ">";
    [ObservableProperty] private string ruleEditorValue = string.Empty;
    [ObservableProperty] private string ruleEditorEvaluationType = "mark_matches";
    [ObservableProperty] private bool ruleEditorUseTiers;
    [ObservableProperty] private string ruleEditorComparison = ">=";
    [ObservableProperty] private string ruleEditorTarget = string.Empty;
    [ObservableProperty] private string ruleEditorSuccessActionType = "mark";
    [ObservableProperty] private string ruleEditorSuccessAmount = string.Empty;
    [ObservableProperty] private string ruleEditorSuccessCurrency = "NIO";
    [ObservableProperty] private string ruleEditorSuccessValue = string.Empty;
    [ObservableProperty] private string ruleEditorFailureActionType = "none";
    [ObservableProperty] private string ruleEditorFailureAmount = string.Empty;
    [ObservableProperty] private string ruleEditorFailureCurrency = "NIO";
    [ObservableProperty] private string ruleEditorFailureValue = string.Empty;
    [ObservableProperty] private string multiDescriptionMetric = string.Empty;
    [ObservableProperty] private string multiDescriptionOperator = ">=";
    [ObservableProperty] private string multiDescriptionTargetPerDescription = string.Empty;
    [ObservableProperty] private string multiDescriptionMinimumCount = string.Empty;
    [ObservableProperty] private string multiDescriptionSingleReward = string.Empty;
    [ObservableProperty] private string ruleEditorTierName = string.Empty;
    [ObservableProperty] private string ruleEditorTierOperator = ">=";
    [ObservableProperty] private string ruleEditorTierValue = string.Empty;
    [ObservableProperty] private string ruleEditorTierActionType = "reward";
    [ObservableProperty] private string ruleEditorTierAmount = string.Empty;
    [ObservableProperty] private string ruleEditorTierCurrency = "NIO";
    [ObservableProperty] private string ruleEditorTierResultValue = string.Empty;

    public string BuilderColumnSummary => SelectedTemplateColumns.Count == 0
        ? "Selecciona las columnas visibles de la plantilla."
        : $"{SelectedTemplateColumns.Count} columnas seleccionadas. Se guardan en column_design como CSV compatible.";

    public string BuilderColumnModelSummary
    {
        get
        {
            var loadedColumns = AvailableTemplateColumns
                .Concat(SelectedTemplateColumns)
                .Select(item => item.Column)
                .ToList();

            if (loadedColumns.Count == 0)
                return "Aun no hay metadata de columnas cargada.";

            var baseColumns = loadedColumns.Where(column => !column.VisibleInAdvancedMode).ToList();
            var dimensions = baseColumns.Count(IsBuilderDimensionColumn);
            var metrics = baseColumns.Count(IsBuilderMetricColumn);
            var advanced = loadedColumns.Count(column => column.VisibleInAdvancedMode);

            return advanced > 0
                ? $"Modelo BI cargado: {dimensions} dimensiones y {metrics} metricas reales ({advanced} campo(s) en modo avanzado)."
                : $"Modelo BI cargado: {dimensions} dimensiones y {metrics} metricas reales.";
        }
    }

    public string BuilderMetadataSummary => RuleDimensionChoices.Count == 0 && RuleMetricChoices.Count == 0
        ? "Selecciona un Source para cargar columnas, dimensiones y metricas reales."
        : $"{RuleDimensionChoices.Count} dimensiones y {RuleMetricChoices.Count} metricas disponibles, alineadas con columnas seleccionadas y metadata real.";

    public string RuleMetricHint => RuleMetricChoices.Count == 0
        ? "No hay metricas disponibles. Carga metadata del Source."
        : $"Metricas disponibles: {RuleMetricChoices.Count}. Puedes escribir para buscar (ej: %MD_COB, MD_COB, CORDOBAS).";

    public string RuleEditorTypePresetHelp => RuleEditorTypePreset switch
    {
        "per_description" => "Cada descripcion puede pagar un monto individual cuando cumple la meta.",
        "multi_description" => "Se paga un monto unico cuando se cumple la cantidad minima de descripciones.",
        "tiered" => "Aplica niveles por cumplimiento (escalonado).",
        _ => "Regla base por indicador con accion directa."
    };

    public bool IsSimpleTypePresetSelected => string.Equals(RuleEditorTypePreset, "simple", StringComparison.OrdinalIgnoreCase);
    public bool IsPerDescriptionTypePresetSelected => string.Equals(RuleEditorTypePreset, "per_description", StringComparison.OrdinalIgnoreCase);
    public bool IsMultiDescriptionTypePresetSelected => string.Equals(RuleEditorTypePreset, "multi_description", StringComparison.OrdinalIgnoreCase);
    public bool IsTieredTypePresetSelected => string.Equals(RuleEditorTypePreset, "tiered", StringComparison.OrdinalIgnoreCase);

    public bool IsRuleTypeSimpleVisible => IsSimpleTypePresetSelected;
    public bool IsRuleTypePerDescriptionVisible => IsPerDescriptionTypePresetSelected;
    public bool IsRuleTypeMultiDescriptionVisible => IsMultiDescriptionTypePresetSelected;
    public bool IsRuleTypeTieredVisible => IsTieredTypePresetSelected;
    public bool IsRuleTierToggleVisible => !IsTieredTypePresetSelected;
    public bool IsRuleSuccessSectionByPresetVisible => !IsTieredTypePresetSelected;
    public bool IsRuleTierSectionByPresetVisible => IsTieredTypePresetSelected;
    public bool IsRuleSuccessCardVisible => IsSimpleTypePresetSelected;
    public bool IsRuleFailureCardVisible => IsSimpleTypePresetSelected || IsTieredTypePresetSelected;

    public string BuilderRulesSummary => ConfiguredRules.Count == 0
        ? "Aun no hay reglas configuradas."
        : $"{ConfiguredRules.Count} reglas guiadas listas (generales: {ConfiguredGeneralRules.Count}, escalonadas: {ConfiguredTieredRules.Count}).";

    public string BuilderGeneralRulesSummary => ConfiguredGeneralRules.Count == 0
        ? "No hay reglas generales configuradas."
        : $"{ConfiguredGeneralRules.Count} regla(s) general(es).";

    public string BuilderTieredRulesSummary => ConfiguredTieredRules.Count == 0
        ? "No hay reglas escalonadas configuradas."
        : $"{ConfiguredTieredRules.Count} regla(s) escalonada(s).";

    public string RuleEditorTierSummary => RuleEditorTiers.Count == 0
        ? "Agrega niveles para premiar o penalizar segun el cumplimiento alcanzado."
        : $"{RuleEditorTiers.Count} nivel(es) escalonados configurados. El ultimo nivel cumplido sera el resultado aplicado.";

    public string RuleEditorLiveSummary
    {
        get
        {
            if (IsPerDescriptionTypePresetSelected)
            {
                var configured = PerDescriptionRuleLines.Count(line =>
                    !string.IsNullOrWhiteSpace(line.Description) && !string.IsNullOrWhiteSpace(line.Amount));

                return configured > 0
                    ? "Si una descripcion cumple, gana el monto configurado para esa descripcion."
                    : "Configura al menos una descripcion con su monto para armar la regla.";
            }

            if (IsMultiDescriptionTypePresetSelected)
            {
                var countText = string.IsNullOrWhiteSpace(MultiDescriptionMinimumCount) ? "N" : MultiDescriptionMinimumCount.Trim();
                var rewardText = string.IsNullOrWhiteSpace(MultiDescriptionSingleReward) ? "monto definido" : MultiDescriptionSingleReward.Trim();
                return $"Si {countText} descripciones cumplen la meta, ganas {rewardText}.";
            }

            if (IsTieredTypePresetSelected)
            {
                var tiers = RuleEditorTiers.Count;
                return tiers > 0
                    ? $"Regla escalonada con {tiers} nivel(es) configurados."
                    : "Regla escalonada lista para configurar niveles.";
            }

            var operatorText = string.IsNullOrWhiteSpace(RuleEditorOperator) ? ">=" : RuleEditorOperator.Trim();
            var valueText = string.IsNullOrWhiteSpace(RuleEditorValue) ? "N" : RuleEditorValue.Trim();
            var rewardSummaryText = string.IsNullOrWhiteSpace(RuleEditorSuccessAmount) ? "resultado configurado" : RuleEditorSuccessAmount.Trim();
            return $"Si el indicador es {operatorText} {valueText}, ganas {rewardSummaryText}.";
        }
    }

    public bool IsRuleEditorNameValidationVisible => !string.IsNullOrWhiteSpace(RuleEditorNameValidationMessage);
    public bool IsRuleEditorMetricValidationVisible => !string.IsNullOrWhiteSpace(RuleEditorMetricValidationMessage);
    public bool IsRuleEditorConditionValidationVisible => !string.IsNullOrWhiteSpace(RuleEditorConditionValidationMessage);
    public bool IsRuleEditorRewardValidationVisible => !string.IsNullOrWhiteSpace(RuleEditorRewardValidationMessage);
    public bool IsPerDescriptionDuplicateValidationVisible => !string.IsNullOrWhiteSpace(PerDescriptionDuplicateValidationMessage);

    public string PreservedLegacyRulesSummary => PreservedLegacyRules.Count == 0
        ? string.Empty
        : $"{PreservedLegacyRules.Count} reglas heredadas se conservaran sin cambios al guardar.";

    public bool CanConfigureBuilderRules => RuleDimensionChoices.Count > 0 && RuleMetricChoices.Count > 0;
    public bool CanDeleteSelectedRule => SelectedConfiguredRule != null;
    public bool CanSaveRuleEditor =>
        CanConfigureBuilderRules
        && !IsRuleEditorNameValidationVisible
        && !IsRuleEditorMetricValidationVisible
        && !IsRuleEditorConditionValidationVisible
        && !IsRuleEditorRewardValidationVisible
        && !IsPerDescriptionDuplicateValidationVisible;

    public bool IsRuleEditorTargetVisible =>
        IsMultiDescriptionTypePresetSelected && !RuleEditorUseTiers && RequiresBuilderTarget(RuleEditorEvaluationType);

    public bool IsRuleEditorTierSectionVisible => RuleEditorUseTiers;

    public bool IsRuleEditorSuccessSectionVisible => !RuleEditorUseTiers;

    public string RuleEditorFailureSectionTitle => RuleEditorUseTiers
        ? "Resultado si no alcanza ningun nivel"
        : "Resultado si no cumple";

    public bool IsRuleEditorSuccessAmountVisible => RequiresBuilderAmount(RuleEditorSuccessActionType);

    public bool IsRuleEditorSuccessValueVisible => RequiresBuilderText(RuleEditorSuccessActionType);

    public bool IsRuleEditorFailureAmountVisible => RequiresBuilderAmount(RuleEditorFailureActionType);

    public bool IsRuleEditorFailureValueVisible => RequiresBuilderText(RuleEditorFailureActionType);

    public bool IsRuleEditorTierAmountVisible => RequiresBuilderAmount(RuleEditorTierActionType);

    public bool IsRuleEditorTierValueVisible => RequiresBuilderText(RuleEditorTierActionType);

    public bool IsPreservedLegacyRulesVisible => PreservedLegacyRules.Count > 0;

    public async Task InitializeBuilderAsync()
    {
        if (_builderInitialized)
            return;

        _builderInitialized = true;
        LoadBuilderStaticOptions();
        EnsurePerDescriptionRuleLines();
        UpdateBuilderEditorVisibility();
        await BuilderRefreshAsync();
    }

    [RelayCommand]
    private void SelectRuleTypePreset(string? preset)
    {
        RuleEditorTypePreset = NormalizeRuleTypePresetKey(preset);
    }

    [RelayCommand]
    private async Task BuilderRefreshAsync()
    {
        try
        {
            IsBusy = true;
            await RefreshBuilderMetadataAsync();
            await LoadBuilderTemplatesAsync();

            if (!string.IsNullOrWhiteSpace(Source))
                await ReloadBuilderMetadataAsync(true);

            StatusMessage = "Plantillas y metadata actualizadas.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo refrescar Builder de Custom Report.");
            StatusMessage = $"No se pudo refrescar: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task StartNewTemplateAsync()
    {
        SelectedTemplate = null;
        ReportName = "Nueva plantilla";
        Source = SelectedReportType?.Key ?? Source;
        ColumnDesign = string.Empty;
        ClearBuilderRules();

        if (!string.IsNullOrWhiteSpace(Source) && IsDatasetConfigured)
            await ReloadBuilderMetadataAsync(false);
        else
            ClearBuilderMetadata();

        StatusMessage = "Plantilla nueva lista para editar.";
    }

    [RelayCommand]
    private async Task OpenSelectedTemplateAsync()
    {
        if (SelectedTemplate == null)
        {
            StatusMessage = "Selecciona una plantilla para cargar.";
            return;
        }

        try
        {
            IsBusy = true;
            await ApplyTemplateToBuilderAsync(SelectedTemplate);
            StatusMessage = $"Plantilla '{SelectedTemplate.Nombre}' cargada.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo cargar plantilla en el builder.");
            StatusMessage = $"No se pudo cargar: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveCurrentTemplateAsync()
    {
        if (string.IsNullOrWhiteSpace(ReportName))
        {
            StatusMessage = "El nombre de la plantilla es obligatorio.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Source))
        {
            StatusMessage = "El Source es obligatorio.";
            return;
        }

        try
        {
            IsBusy = true;
            var template = BuildBuilderTemplate();
            var saved = await _dynamicReportService.SaveTemplateAsync(template);
            await LoadBuilderTemplatesAsync();
            SelectedTemplate = Templates.FirstOrDefault(item => item.Id == saved.Id);
            StatusMessage = $"Plantilla '{saved.Nombre}' guardada.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo guardar plantilla desde el builder.");
            StatusMessage = $"No se pudo guardar: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DuplicateCurrentTemplateAsync()
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
            await LoadBuilderTemplatesAsync();
            SelectedTemplate = Templates.FirstOrDefault(item => item.Id == copy.Id);
            if (SelectedTemplate != null)
                await ApplyTemplateToBuilderAsync(SelectedTemplate);

            StatusMessage = $"Plantilla duplicada: {copy.Nombre}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo duplicar plantilla desde el builder.");
            StatusMessage = $"No se pudo duplicar: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteCurrentTemplateAsync()
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
            await LoadBuilderTemplatesAsync();
            await StartNewTemplateAsync();
            StatusMessage = $"Plantilla '{deletedName}' eliminada.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo eliminar plantilla desde el builder.");
            StatusMessage = $"No se pudo eliminar: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand] private void AddCheckedTemplateColumns() => MoveTemplateColumnsToSelected(GetCheckedAvailableColumns());
    [RelayCommand] private void AddVisibleTemplateColumns() => MoveTemplateColumnsToSelected(FilteredAvailableTemplateColumns.ToList());
    [RelayCommand] private void RemoveCheckedTemplateColumns() => MoveTemplateColumnsToAvailable(GetCheckedSelectedColumns());
    [RelayCommand] private void RemoveVisibleTemplateColumns() => MoveTemplateColumnsToAvailable(FilteredSelectedTemplateColumns.ToList());
    [RelayCommand] private void ClearSelectedTemplateColumns() => MoveTemplateColumnsToAvailable(SelectedTemplateColumns.ToList());

    [RelayCommand]
    private void MoveTemplateColumnUp()
        => MoveBuilderSelectedColumn(-1);

    [RelayCommand]
    private void MoveTemplateColumnDown()
        => MoveBuilderSelectedColumn(1);

    [RelayCommand]
    private void AddPerDescriptionRuleLine()
    {
        PerDescriptionRuleLines.Add(new DescriptionAmountRuleLineUiModel());
        OnPropertyChanged(nameof(RuleEditorLiveSummary));
        UpdateRuleEditorSoftValidations();
    }

    [RelayCommand]
    private void RemovePerDescriptionRuleLine(DescriptionAmountRuleLineUiModel? line)
    {
        if (line == null)
            return;

        PerDescriptionRuleLines.Remove(line);
        if (PerDescriptionRuleLines.Count == 0)
            AddPerDescriptionRuleLine();

        OnPropertyChanged(nameof(RuleEditorLiveSummary));
        UpdateRuleEditorSoftValidations();
    }

    [RelayCommand]
    private void AddMultiDescriptionRouteException()
    {
        MultiDescriptionRouteExceptions.Add(new MultiDescriptionRouteExceptionUiModel());
        UpdateRuleEditorSoftValidations();
    }

    [RelayCommand]
    private void RemoveMultiDescriptionRouteException(MultiDescriptionRouteExceptionUiModel? exception)
    {
        if (exception == null)
            return;

        MultiDescriptionRouteExceptions.Remove(exception);
        UpdateRuleEditorSoftValidations();
    }

    [RelayCommand]
    private void SaveRuleBuilderRule()
    {
        UpdateRuleEditorSoftValidations();
        if (IsRuleEditorNameValidationVisible
            || IsRuleEditorMetricValidationVisible
            || IsRuleEditorConditionValidationVisible
            || IsRuleEditorRewardValidationVisible
            || IsPerDescriptionDuplicateValidationVisible)
        {
            StatusMessage = "Revisa los campos marcados para completar la regla.";
            return;
        }

        if (!CanConfigureBuilderRules)
        {
            StatusMessage = "Primero carga metadata del Source para construir reglas.";
            return;
        }

        if (string.IsNullOrWhiteSpace(RuleEditorDimension) || string.IsNullOrWhiteSpace(RuleEditorMetric))
        {
            StatusMessage = "Selecciona sobre que dimension aplica y que metrica se evalua.";
            return;
        }

        if (!TryParseDecimal(RuleEditorValue, out var thresholdValue))
        {
            StatusMessage = "El valor de la condicion debe ser numerico.";
            return;
        }

        decimal? target = null;
        if (!RuleEditorUseTiers && RequiresBuilderTarget(RuleEditorEvaluationType))
        {
            if (!TryParseDecimal(RuleEditorTarget, out var targetValue))
            {
                StatusMessage = "La meta requerida debe ser numerica.";
                return;
            }

            target = targetValue;
        }

        if (RuleEditorUseTiers && NormalizeBuilderKey(RuleEditorEvaluationType) == "MARKMATCHES")
        {
            StatusMessage = "Los niveles escalonados requieren una evaluacion por cantidad o total, no solo marcado visual.";
            return;
        }

        if (RuleEditorUseTiers && RuleEditorTiers.Count == 0)
        {
            StatusMessage = "Agrega al menos un nivel escalonado antes de guardar la regla.";
            return;
        }

        var successAction = new GuidedRuleActionUiModel { Type = "none", Currency = "NIO" };
        string error;
        if (!RuleEditorUseTiers
            && !TryBuildBuilderAction(RuleEditorSuccessActionType, RuleEditorSuccessAmount, RuleEditorSuccessCurrency, RuleEditorSuccessValue, out successAction, out error))
        {
            StatusMessage = error;
            return;
        }

        if (!TryBuildBuilderAction(RuleEditorFailureActionType, RuleEditorFailureAmount, RuleEditorFailureCurrency, RuleEditorFailureValue, out var failureAction, out error))
        {
            StatusMessage = error;
            return;
        }

        var dimension = FindBuilderFieldOption(RuleDimensionChoices, RuleEditorDimension);
        var metric = FindBuilderFieldOption(RuleMetricChoices, RuleEditorMetric);
        var rule = new GuidedRuleUiModel
        {
            Id = SelectedConfiguredRule?.Id ?? Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(RuleEditorName) ? BuildBuilderRuleName(dimension, metric, thresholdValue) : RuleEditorName.Trim(),
            Dimension = RuleEditorDimension,
            DimensionLabel = dimension?.DisplayName ?? RuleEditorDimension,
            Metric = RuleEditorMetric,
            MetricLabel = metric?.DisplayName ?? RuleEditorMetric,
            Operator = NormalizeBuilderOperator(RuleEditorOperator),
            Value = thresholdValue,
            EvaluationType = RuleEditorEvaluationType,
            Comparison = NormalizeBuilderOperator(RuleEditorComparison, ">="),
            Target = target,
            SuccessAction = successAction,
            FailureAction = failureAction,
            Tiers = new ObservableCollection<GuidedRuleTierUiModel>(RuleEditorTiers.Select(CloneBuilderTier))
        };

        if (SelectedConfiguredRule == null)
        {
            ConfiguredRules.Add(rule);
        }
        else
        {
            var index = ConfiguredRules.IndexOf(SelectedConfiguredRule);
            if (index >= 0)
                ConfiguredRules[index] = rule;
            else
                ConfiguredRules.Add(rule);
        }

        SelectedConfiguredRule = rule;
        RefreshRuleBuckets();
        StatusMessage = "Regla guardada.";
    }

    [RelayCommand]
    private void SaveRuleTier()
    {
        if (!RuleEditorUseTiers)
        {
            StatusMessage = "Activa niveles escalonados para agregar tiers.";
            return;
        }

        if (!TryParseDecimal(RuleEditorTierValue, out var tierValue))
        {
            StatusMessage = "El valor del nivel debe ser numerico.";
            return;
        }

        if (!TryBuildBuilderAction(
                RuleEditorTierActionType,
                RuleEditorTierAmount,
                RuleEditorTierCurrency,
                RuleEditorTierResultValue,
                out var resultAction,
                out var error))
        {
            StatusMessage = error;
            return;
        }

        if (resultAction.Type == "none")
        {
            StatusMessage = "Cada nivel debe tener un resultado configurado.";
            return;
        }

        var tier = new GuidedRuleTierUiModel
        {
            Name = string.IsNullOrWhiteSpace(RuleEditorTierName) ? string.Empty : RuleEditorTierName.Trim(),
            Operator = NormalizeBuilderOperator(RuleEditorTierOperator, ">="),
            Value = tierValue,
            Result = resultAction
        };

        if (SelectedRuleEditorTier == null)
        {
            RuleEditorTiers.Add(tier);
        }
        else
        {
            var index = RuleEditorTiers.IndexOf(SelectedRuleEditorTier);
            if (index >= 0)
                RuleEditorTiers[index] = tier;
            else
                RuleEditorTiers.Add(tier);
        }

        SelectedRuleEditorTier = tier;
        OnPropertyChanged(nameof(RuleEditorTierSummary));
        StatusMessage = "Nivel escalonado guardado.";
    }

    [RelayCommand]
    private void DeleteRuleTier()
    {
        if (SelectedRuleEditorTier == null)
            return;

        RuleEditorTiers.Remove(SelectedRuleEditorTier);
        ClearRuleTierEditor();
        OnPropertyChanged(nameof(RuleEditorTierSummary));
        StatusMessage = "Nivel escalonado eliminado.";
    }

    [RelayCommand]
    private void ClearRuleTierEditor()
    {
        SelectedRuleEditorTier = null;
        RuleEditorTierName = string.Empty;
        RuleEditorTierOperator = ">=";
        RuleEditorTierValue = string.Empty;
        RuleEditorTierActionType = "reward";
        RuleEditorTierAmount = string.Empty;
        RuleEditorTierCurrency = "NIO";
        RuleEditorTierResultValue = string.Empty;
        UpdateBuilderEditorVisibility();
    }

    [RelayCommand]
    private void MoveRuleTierUp()
        => MoveBuilderTier(-1);

    [RelayCommand]
    private void MoveRuleTierDown()
        => MoveBuilderTier(1);

    [RelayCommand]
    private void DeleteRuleBuilderRule()
    {
        if (SelectedConfiguredRule == null)
            return;

        ConfiguredRules.Remove(SelectedConfiguredRule);
        ClearRuleBuilderEditor();
        RefreshRuleBuckets();
        StatusMessage = "Regla eliminada.";
    }

    [RelayCommand]
    private void ClearRuleBuilderEditor()
    {
        SelectedConfiguredRule = null;
        RuleEditorName = string.Empty;
        RuleEditorNameValidationMessage = string.Empty;
        RuleEditorMetricValidationMessage = string.Empty;
        RuleEditorConditionValidationMessage = string.Empty;
        RuleEditorRewardValidationMessage = string.Empty;
        PerDescriptionDuplicateValidationMessage = string.Empty;
        RuleEditorOperator = ">";
        RuleEditorValue = string.Empty;
        RuleEditorEvaluationType = "mark_matches";
        RuleEditorUseTiers = false;
        RuleEditorTypePreset = "simple";
        RuleEditorComparison = ">=";
        RuleEditorTarget = string.Empty;
        RuleEditorSuccessActionType = "mark";
        RuleEditorSuccessAmount = string.Empty;
        RuleEditorSuccessCurrency = "NIO";
        RuleEditorSuccessValue = string.Empty;
        RuleEditorFailureActionType = "none";
        RuleEditorFailureAmount = string.Empty;
        RuleEditorFailureCurrency = "NIO";
        RuleEditorFailureValue = string.Empty;
        MultiDescriptionMetric = string.Empty;
        MultiDescriptionOperator = ">=";
        MultiDescriptionTargetPerDescription = string.Empty;
        MultiDescriptionMinimumCount = string.Empty;
        MultiDescriptionSingleReward = string.Empty;
        MultiDescriptionRouteExceptions.Clear();
        RuleEditorTiers.Clear();
        PerDescriptionRuleLines.Clear();
        EnsurePerDescriptionRuleLines();
        ClearRuleTierEditor();
        EnsureBuilderRuleSelections();
        UpdateBuilderEditorVisibility();
        UpdateRuleEditorSoftValidations();
    }

    partial void OnSelectedConfiguredRuleChanged(GuidedRuleUiModel? value)
    {
        OnPropertyChanged(nameof(CanDeleteSelectedRule));
        if (value == null)
            return;

        RuleEditorName = value.Name;
        RuleEditorDimension = value.Dimension;
        RuleEditorMetric = value.Metric;
        RuleEditorOperator = value.Operator;
        RuleEditorValue = value.Value.ToString(CultureInfo.InvariantCulture);
        RuleEditorEvaluationType = value.EvaluationType;
        RuleEditorUseTiers = value.Tiers.Count > 0;
        RuleEditorTypePreset = value.Tiers.Count > 0 ? "tiered" : "simple";
        RuleEditorComparison = value.Comparison;
        RuleEditorTarget = value.Target?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        RuleEditorSuccessActionType = value.SuccessAction.Type;
        RuleEditorSuccessAmount = value.SuccessAction.Amount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        RuleEditorSuccessCurrency = string.IsNullOrWhiteSpace(value.SuccessAction.Currency) ? "NIO" : value.SuccessAction.Currency;
        RuleEditorSuccessValue = value.SuccessAction.Value;
        RuleEditorFailureActionType = value.FailureAction.Type;
        RuleEditorFailureAmount = value.FailureAction.Amount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        RuleEditorFailureCurrency = string.IsNullOrWhiteSpace(value.FailureAction.Currency) ? "NIO" : value.FailureAction.Currency;
        RuleEditorFailureValue = value.FailureAction.Value;
        Replace(RuleEditorTiers, value.Tiers.Select(CloneBuilderTier));
        ClearRuleTierEditor();
        OnPropertyChanged(nameof(RuleEditorTierSummary));
        UpdateBuilderEditorVisibility();
    }

    partial void OnRuleEditorEvaluationTypeChanged(string value) => UpdateBuilderEditorVisibility();
    partial void OnRuleEditorNameChanged(string value) => UpdateRuleEditorSoftValidations();
    partial void OnRuleEditorMetricChanged(string value)
    {
        OnPropertyChanged(nameof(RuleEditorLiveSummary));
        UpdateRuleEditorSoftValidations();
    }
    partial void OnRuleEditorOperatorChanged(string value) => UpdateRuleEditorSoftValidations();
    partial void OnRuleEditorValueChanged(string value)
    {
        OnPropertyChanged(nameof(RuleEditorLiveSummary));
        UpdateRuleEditorSoftValidations();
    }
    partial void OnRuleEditorSuccessAmountChanged(string value)
    {
        OnPropertyChanged(nameof(RuleEditorLiveSummary));
        UpdateRuleEditorSoftValidations();
    }
    partial void OnMultiDescriptionMetricChanged(string value) => UpdateRuleEditorSoftValidations();
    partial void OnMultiDescriptionOperatorChanged(string value) => UpdateRuleEditorSoftValidations();
    partial void OnMultiDescriptionTargetPerDescriptionChanged(string value) => UpdateRuleEditorSoftValidations();
    partial void OnMultiDescriptionMinimumCountChanged(string value)
    {
        OnPropertyChanged(nameof(RuleEditorLiveSummary));
        UpdateRuleEditorSoftValidations();
    }
    partial void OnMultiDescriptionSingleRewardChanged(string value)
    {
        OnPropertyChanged(nameof(RuleEditorLiveSummary));
        UpdateRuleEditorSoftValidations();
    }
    partial void OnRuleEditorTypePresetChanged(string value)
    {
        RuleEditorTypePreset = NormalizeRuleTypePresetKey(value);

        if (IsTieredTypePresetSelected)
        {
            RuleEditorUseTiers = true;
            if (NormalizeBuilderKey(RuleEditorEvaluationType) == "MARKMATCHES")
                RuleEditorEvaluationType = "count_matches";
        }
        else if (IsMultiDescriptionTypePresetSelected)
        {
            RuleEditorUseTiers = false;
            RuleEditorEvaluationType = "threshold_by_count";
        }
        else if (IsPerDescriptionTypePresetSelected)
        {
            RuleEditorUseTiers = false;
            RuleEditorEvaluationType = "count_matches";
        }
        else
        {
            RuleEditorUseTiers = false;
        }

        OnPropertyChanged(nameof(RuleEditorTypePresetHelp));
        OnPropertyChanged(nameof(IsSimpleTypePresetSelected));
        OnPropertyChanged(nameof(IsPerDescriptionTypePresetSelected));
        OnPropertyChanged(nameof(IsMultiDescriptionTypePresetSelected));
        OnPropertyChanged(nameof(IsTieredTypePresetSelected));
        UpdateBuilderEditorVisibility();
    }
    partial void OnRuleEditorUseTiersChanged(bool value)
    {
        if (value && NormalizeBuilderKey(RuleEditorEvaluationType) == "MARKMATCHES")
            RuleEditorEvaluationType = "count_matches";

        if (!value)
        {
            RuleEditorTiers.Clear();
            ClearRuleTierEditor();
            OnPropertyChanged(nameof(RuleEditorTierSummary));
        }

        UpdateBuilderEditorVisibility();
    }
    partial void OnRuleEditorSuccessActionTypeChanged(string value) => UpdateBuilderEditorVisibility();
    partial void OnRuleEditorFailureActionTypeChanged(string value) => UpdateBuilderEditorVisibility();
    partial void OnRuleEditorTierActionTypeChanged(string value) => UpdateBuilderEditorVisibility();
    partial void OnColumnViewModeChanged(string value) => RefreshBuilderColumnViews();

    partial void OnSelectedRuleEditorTierChanged(GuidedRuleTierUiModel? value)
    {
        if (value == null)
            return;

        RuleEditorTierName = value.Name;
        RuleEditorTierOperator = value.Operator;
        RuleEditorTierValue = value.Value.ToString(CultureInfo.InvariantCulture);
        RuleEditorTierActionType = value.Result.Type;
        RuleEditorTierAmount = value.Result.Amount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        RuleEditorTierCurrency = string.IsNullOrWhiteSpace(value.Result.Currency) ? "NIO" : value.Result.Currency;
        RuleEditorTierResultValue = value.Result.Value;
        UpdateBuilderEditorVisibility();
    }

    private async Task RefreshBuilderMetadataAsync()
    {
        var selection = await _defaultSelectionService.EnsureSelectionStateAsync(resolveNames: false);
        ActiveDatasetId = selection.DatasetId;
        IsDatasetConfigured = !string.IsNullOrWhiteSpace(ActiveDatasetId);

        ReportTypes.Clear();
        if (!IsDatasetConfigured)
        {
            StatusMessage = "Dataset no configurado. Puedes escribir Source manualmente, pero no se cargara metadata automatica.";
            return;
        }

        foreach (var reportType in await _dynamicReportService.GetReportTypesAsync(ActiveDatasetId))
            ReportTypes.Add(reportType);

        if (string.IsNullOrWhiteSpace(Source) && ReportTypes.Count > 0)
            Source = ReportTypes[0].Key;
    }

    private async Task LoadBuilderTemplatesAsync()
    {
        Templates.Clear();
        foreach (var template in await _dynamicReportService.GetTemplatesAsync())
            Templates.Add(template);
    }

    private async Task ApplyTemplateToBuilderAsync(ReportTemplate template)
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

        await ReloadBuilderMetadataAsync(false);

        var selectedKeys = ParseColumnDesign(template.ColumnDesign);
        if (selectedKeys.Count == 0 && template.Columnas.Count > 0)
        {
            selectedKeys = template.Columnas
                .Select(column => string.IsNullOrWhiteSpace(column.SourceField) ? column.Key : column.SourceField)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
        }

        if (AvailableTemplateColumns.Count + SelectedTemplateColumns.Count > 0)
            ApplyBuilderSelectedColumns(selectedKeys, false);
        else if (template.Columnas.Count > 0)
            LoadBuilderColumns(template.Columnas, selectedKeys, false);

        if (RuleDimensionChoices.Count == 0 && template.Columnas.Count > 0)
            LoadBuilderRuleCatalog(BuildBuilderFieldCatalog(template.Columnas));

        LoadBuilderRules(template.Ruth);
    }

    private async Task ReloadBuilderMetadataSafeAsync(bool preserveSelection)
    {
        try
        {
            await ReloadBuilderMetadataAsync(preserveSelection);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo recargar metadata del builder.");
            StatusMessage = $"No se pudo cargar metadata del Source: {ex.Message}";
        }
    }

    private async Task ReloadBuilderMetadataAsync(bool preserveSelection)
    {
        if (!IsDatasetConfigured || string.IsNullOrWhiteSpace(Source))
        {
            ClearBuilderMetadata();
            return;
        }

        var selectedKeys = preserveSelection
            ? SelectedTemplateColumns.Select(item => ResolveBuilderColumnValue(item.Column)).ToList()
            : new List<string>();

        var columnCatalog = await _dynamicReportService.GetColumnCatalogAsync(ActiveDatasetId, Source.Trim(), DateTime.Now.Year, DateTime.Now.Month, vendedores: null);

        try
        {
            _builderFieldCatalog = await _dynamicReportService.GetFieldCatalogAsync(
                ActiveDatasetId,
                ReportSourceMode.ExistingReport,
                Source.Trim(),
                DateTime.Now.Year,
                DateTime.Now.Month,
                vendedores: null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo cargar catalogo de campos completo. Se usara fallback desde columnas.");
            _builderFieldCatalog = BuildBuilderFieldCatalog(columnCatalog);
        }

        LoadBuilderColumns(columnCatalog, selectedKeys, true);
        LoadBuilderRuleCatalog(_builderFieldCatalog);
    }

    private void LoadBuilderColumns(IReadOnlyList<ReportColumnDefinition> catalog, IReadOnlyList<string> selectedKeys, bool autoFillDefault)
    {
        var remaining = catalog
            .Where(column => !IsArtificialUiColumn(column))
            .Where(column => column.VisibleInColumnSelector || column.VisibleInAdvancedMode)
            .OrderBy(column => column.VisibleInAdvancedMode ? 1 : 0)
            .ThenBy(GetBuilderColumnCategoryOrder)
            .ThenBy(column => column.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(column => column.Order)
            .Select(column => new ReportColumnPickerItem(CloneColumn(column)))
            .ToList();

        var selected = new List<ReportColumnPickerItem>();
        foreach (var key in selectedKeys)
        {
            var match = TakeBuilderColumnMatch(remaining, key);
            if (match != null)
                selected.Add(match);
        }

        if (selected.Count == 0 && autoFillDefault)
        {
            var defaults = remaining
                .Where(item => !item.Column.VisibleInAdvancedMode)
                .Take(Math.Min(6, remaining.Count))
                .ToList();

            if (defaults.Count == 0)
                defaults = remaining.Take(Math.Min(6, remaining.Count)).ToList();

            foreach (var item in defaults)
            {
                selected.Add(item);
                remaining.Remove(item);
            }
        }

        Replace(SelectedTemplateColumns, selected);
        Replace(AvailableTemplateColumns, remaining);
        NormalizeBuilderSelectedColumns();
    }

    private void ApplyBuilderSelectedColumns(IReadOnlyList<string> selectedKeys, bool autoFillDefault)
    {
        var catalog = AvailableTemplateColumns
            .Concat(SelectedTemplateColumns)
            .Select(item => CloneColumn(item.Column))
            .ToList();

        if (catalog.Count == 0)
            return;

        LoadBuilderColumns(catalog, selectedKeys, autoFillDefault);
    }

    private void LoadBuilderRuleCatalog(IReadOnlyList<ReportFieldDefinition> fieldCatalog)
    {
        _builderFieldCatalog = fieldCatalog ?? Array.Empty<ReportFieldDefinition>();
        RefreshBuilderRuleCatalogFromSelection();
    }

    private void RefreshBuilderRuleCatalogFromSelection()
    {
        var selectedFields = BuildRuleCatalogFromSelectedColumns();
        var effectiveCatalog = selectedFields.Count > 0
            ? selectedFields
            : _builderFieldCatalog
                .Where(field => !IsArtificialUiField(field))
                .ToList();

        if (effectiveCatalog.Count == 0)
        {
            RuleDimensionChoices.Clear();
            RuleMetricChoices.Clear();
            OnPropertyChanged(nameof(BuilderMetadataSummary));
            OnPropertyChanged(nameof(CanConfigureBuilderRules));
            OnPropertyChanged(nameof(RuleMetricHint));
            return;
        }

        var dimensions = BuildBuilderDimensions(effectiveCatalog);
        if (dimensions.Count == 0)
            dimensions = BuildBuilderDimensions(_builderFieldCatalog.Where(field => !IsArtificialUiField(field)));

        var metricsCatalog = effectiveCatalog
            .Concat(_builderFieldCatalog.Where(CanUseAsMetric))
            .Where(field => !IsArtificialUiField(field))
            .DistinctBy(field => string.IsNullOrWhiteSpace(field.CanonicalKey)
                ? NormalizeBuilderIdentity(string.IsNullOrWhiteSpace(field.SourceField) ? field.Key : field.SourceField)
                : field.CanonicalKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var metrics = BuildBuilderMetrics(metricsCatalog);
        if (metrics.Count == 0)
            metrics = BuildBuilderMetrics(_builderFieldCatalog.Where(field => !IsArtificialUiField(field)));

        Replace(RuleDimensionChoices, dimensions);
        Replace(RuleMetricChoices, metrics);
        EnsureBuilderRuleSelections();
        OnPropertyChanged(nameof(BuilderMetadataSummary));
        OnPropertyChanged(nameof(CanConfigureBuilderRules));
        OnPropertyChanged(nameof(RuleMetricHint));
    }

    private List<ReportFieldDefinition> BuildRuleCatalogFromSelectedColumns()
    {
        var loadedColumns = SelectedTemplateColumns
            .Concat(AvailableTemplateColumns)
            .OrderBy(item => item.Column.Order)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (loadedColumns.Count == 0)
            return new List<ReportFieldDefinition>();

        var scoped = new List<ReportFieldDefinition>();
        for (var i = 0; i < loadedColumns.Count; i++)
        {
            var column = loadedColumns[i].Column;
            if (IsArtificialUiColumn(column))
                continue;

            var matchedField = _builderFieldCatalog.FirstOrDefault(field =>
                IsSameBuilderKey(field.SourceField, column.SourceField)
                || IsSameBuilderKey(field.Key, column.SourceField)
                || IsSameBuilderKey(field.DisplayName, column.DisplayName)
                || IsSameBuilderKey(field.SourceField, column.Key)
                || IsSameBuilderKey(field.Key, column.Key));

            scoped.Add(matchedField ?? BuildRuleFieldFromColumn(column, i));
        }

        return scoped
            .Where(field => field.AllowRules)
            .DistinctBy(field => string.IsNullOrWhiteSpace(field.CanonicalKey)
                ? NormalizeBuilderIdentity(string.IsNullOrWhiteSpace(field.SourceField) ? field.Key : field.SourceField)
                : field.CanonicalKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ReportFieldDefinition BuildRuleFieldFromColumn(ReportColumnDefinition column, int index)
    {
        var normalized = NormalizeBuilderKey($"{column.DisplayName} {column.SourceField} {column.Key}");
        var isDimension = column.IsDimension || column.SourceType == ReportColumnSourceType.Dimension;
        var isMeasure = column.IsMeasure || column.SourceType == ReportColumnSourceType.Measure;
        var isCalculated = column.IsCalculated || column.SourceType == ReportColumnSourceType.Calculated;

        if (!isDimension && !isMeasure && !isCalculated)
        {
            if (column.DataType is ReportFieldDataType.Text or ReportFieldDataType.Date or ReportFieldDataType.Boolean || HasDimensionHint(normalized))
                isDimension = true;
            else if (column.DataType is ReportFieldDataType.Integer or ReportFieldDataType.Decimal || HasMetricHint(normalized))
                isMeasure = true;
        }

        return new ReportFieldDefinition
        {
            Key = string.IsNullOrWhiteSpace(column.Key) ? NormalizeBuilderKey(column.SourceField) : column.Key,
            DisplayName = string.IsNullOrWhiteSpace(column.DisplayName) ? column.SourceField : column.DisplayName,
            SourceTable = column.SourceTable,
            SourceField = string.IsNullOrWhiteSpace(column.SourceField) ? column.Key : column.SourceField,
            SourceType = column.SourceType,
            DataType = column.DataType,
            IsMeasure = isMeasure,
            IsDimension = isDimension,
            IsCalculated = isCalculated,
            DefaultFormat = column.DefaultFormat ?? column.FormatString ?? string.Empty,
            AllowSorting = column.AllowSorting,
            AllowFiltering = column.AllowFiltering,
            AllowRules = column.AllowRules,
            DefaultOrder = index,
            VisibleInColumnSelector = column.VisibleInColumnSelector,
            VisibleInRuleScopeSelector = column.IsDimension || column.SourceType == ReportColumnSourceType.Dimension,
            VisibleInRuleMetricSelector = column.IsMeasure || column.SourceType == ReportColumnSourceType.Measure,
            VisibleInAdvancedMode = column.VisibleInAdvancedMode,
            UsageCategory = ResolveBuilderUsageCategory(column),
            CanonicalKey = string.IsNullOrWhiteSpace(column.CatalogCanonicalKey)
                ? NormalizeBuilderIdentity(column.SourceField)
                : column.CatalogCanonicalKey
        };
    }

    private static bool IsArtificialUiColumn(ReportColumnDefinition column)
    {
        if (!column.IsCalculated
            && column.SourceType is not ReportColumnSourceType.Calculated
            and not ReportColumnSourceType.RuleOutput
            and not ReportColumnSourceType.Unknown)
        return false;

        return IsArtificialUiName(column.DisplayName)
               || IsArtificialUiName(column.SourceField)
               || IsArtificialUiName(column.Key);
    }

    private static bool IsArtificialUiField(ReportFieldDefinition field)
    {
        if (!field.IsCalculated
            && field.SourceType is not ReportColumnSourceType.Calculated
            and not ReportColumnSourceType.RuleOutput
            and not ReportColumnSourceType.Unknown)
        return false;

        return IsArtificialUiName(field.DisplayName)
               || IsArtificialUiName(field.SourceField)
               || IsArtificialUiName(field.Key);
    }

    private static bool IsArtificialUiName(string? value)
    {
        var normalized = NormalizeBuilderKey(value);
        return normalized is "SEMAFORO"
            or "CHECK"
            or "ESTADO"
            or "STATUS"
            or "OBSERVACION"
            or "PREMIOCALCULADO"
            or "AFECTACIONCALCULADA"
            or "REGLASGUIADASAPLICADAS";
    }

    private static List<ReportFieldOption> BuildBuilderDimensions(IEnumerable<ReportFieldDefinition> catalog)
    {
        return catalog
            .Where(CanUseAsDimension)
            .Select(ToBuilderFieldOption)
            .DistinctBy(option => string.IsNullOrWhiteSpace(option.CanonicalKey) ? NormalizeBuilderIdentity(option.Value) : option.CanonicalKey, StringComparer.OrdinalIgnoreCase)
            .OrderBy(option => option.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static List<ReportFieldOption> BuildBuilderMetrics(IEnumerable<ReportFieldDefinition> catalog)
    {
        return catalog
            .Where(CanUseAsMetric)
            .Select(ToBuilderFieldOption)
            .DistinctBy(option => string.IsNullOrWhiteSpace(option.CanonicalKey) ? NormalizeBuilderIdentity(option.Value) : option.CanonicalKey, StringComparer.OrdinalIgnoreCase)
            .OrderBy(option => option.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool CanUseAsDimension(ReportFieldDefinition field)
    {
        if (!field.AllowRules)
            return false;

        if (field.VisibleInRuleScopeSelector)
            return true;

        if (field.UsageCategory == ReportFieldUsageCategory.Hidden)
            return false;

        if (field.IsDimension || field.SourceType == ReportColumnSourceType.Dimension)
            return true;

        if (field.DataType is ReportFieldDataType.Text or ReportFieldDataType.Date or ReportFieldDataType.Boolean)
            return true;

        return HasDimensionHint(NormalizeBuilderKey($"{field.DisplayName} {field.SourceField} {field.Key}"));
    }

    private static bool CanUseAsMetric(ReportFieldDefinition field)
    {
        if (!field.AllowRules)
            return false;

        if (field.VisibleInRuleMetricSelector)
            return true;

        if (field.UsageCategory == ReportFieldUsageCategory.Hidden)
            return false;

        if ((field.IsDimension || field.SourceType == ReportColumnSourceType.Dimension)
            && !field.IsMeasure
            && field.SourceType != ReportColumnSourceType.Measure
            && !field.IsCalculated
            && field.SourceType != ReportColumnSourceType.Calculated)
        {
            return false;
        }

        if (field.IsMeasure || field.SourceType == ReportColumnSourceType.Measure)
            return true;

        if (field.IsCalculated || field.SourceType == ReportColumnSourceType.Calculated)
            return true;

        if (field.DataType is ReportFieldDataType.Integer or ReportFieldDataType.Decimal)
            return true;

        var normalized = NormalizeBuilderKey($"{field.DisplayName} {field.SourceField} {field.Key}");
        if (HasMetricHint(normalized))
            return true;

        return false;
    }

    private static bool HasDimensionHint(string normalizedName)
    {
        return normalizedName.Contains("DESCRIPCION", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("SUBGRUPO", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("GRUPO", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("CODIGO", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("CATEGORIA", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("RUTA", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("VENDEDOR", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("PRODUCTO", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("SKU", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasMetricHint(string normalizedName)
    {
        return normalizedName.Contains("COBERTURA", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("PERCENT", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("PORCENTAJE", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("CORDOBAS", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("MONTO", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("VOLUMEN", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("VOLUME", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("UNIDADES", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("UNITS", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("CAJAS", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("TOTAL", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("META", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("MDCOB", StringComparison.OrdinalIgnoreCase);
    }

    private void LoadBuilderRules(IEnumerable<JsonElement> rawRules)
    {
        ConfiguredRules.Clear();
        PreservedLegacyRules.Clear();
        _builderPreservedLegacyRuleNodes.Clear();

        var parseResult = _ruleSchemaService.Parse(rawRules, _builderFieldCatalog);
        foreach (var rule in parseResult.GuidedRules)
            ConfiguredRules.Add(ToBuilderRuleUi(rule));

        foreach (var legacyRule in parseResult.LegacyRuleSummaries)
            PreservedLegacyRules.Add(legacyRule);

        _builderPreservedLegacyRuleNodes = parseResult.PreservedLegacyRules.Select(rule => rule.Clone()).ToList();
        ClearRuleBuilderEditor();

        RefreshBuilderRuleCatalogFromSelection();
        RefreshRuleBuckets();
        OnPropertyChanged(nameof(PreservedLegacyRulesSummary));
        OnPropertyChanged(nameof(IsPreservedLegacyRulesVisible));
    }

    private void MoveTemplateColumnsToSelected(IEnumerable<ReportColumnPickerItem> items)
    {
        var moveList = items.Where(item => AvailableTemplateColumns.Contains(item)).Distinct().ToList();
        if (moveList.Count == 0)
            return;

        foreach (var item in moveList)
        {
            item.IsChecked = false;
            AvailableTemplateColumns.Remove(item);
            SelectedTemplateColumns.Add(item);
        }

        SelectedAvailableTemplateColumn = null;
        NormalizeBuilderSelectedColumns();
    }

    private void MoveTemplateColumnsToAvailable(IEnumerable<ReportColumnPickerItem> items)
    {
        var moveList = items.Where(item => SelectedTemplateColumns.Contains(item)).Distinct().ToList();
        if (moveList.Count == 0)
            return;

        foreach (var item in moveList)
        {
            item.IsChecked = false;
            SelectedTemplateColumns.Remove(item);
            AvailableTemplateColumns.Add(item);
        }

        Replace(
            AvailableTemplateColumns,
            AvailableTemplateColumns
                .OrderBy(item => GetBuilderColumnCategoryOrder(item.Column))
                .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Column.Order)
                .ToList());

        SelectedTemplateColumn = null;
        NormalizeBuilderSelectedColumns();
    }

    private void MoveBuilderSelectedColumn(int delta)
    {
        if (SelectedTemplateColumn == null)
            return;

        var index = SelectedTemplateColumns.IndexOf(SelectedTemplateColumn);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= SelectedTemplateColumns.Count)
            return;

        SelectedTemplateColumns.Move(index, target);
        NormalizeBuilderSelectedColumns();
    }

    private void MoveBuilderTier(int delta)
    {
        if (SelectedRuleEditorTier == null)
            return;

        var index = RuleEditorTiers.IndexOf(SelectedRuleEditorTier);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= RuleEditorTiers.Count)
            return;

        RuleEditorTiers.Move(index, target);
        OnPropertyChanged(nameof(RuleEditorTierSummary));
    }

    private void NormalizeBuilderSelectedColumns()
    {
        for (var i = 0; i < SelectedTemplateColumns.Count; i++)
            SelectedTemplateColumns[i].Column.Order = i;

        ColumnDesign = string.Join(",",
            SelectedTemplateColumns
                .Select(item => ResolveBuilderColumnValue(item.Column))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim()));

        RefreshBuilderColumnViews();
        RefreshBuilderRuleCatalogFromSelection();
        OnPropertyChanged(nameof(BuilderColumnSummary));
        OnPropertyChanged(nameof(BuilderColumnModelSummary));
    }

    private void RefreshBuilderColumnViews()
    {
        Replace(FilteredAvailableTemplateColumns, FilterBuilderColumns(AvailableTemplateColumns, AvailableColumnSearch, ColumnViewMode));
        Replace(FilteredSelectedTemplateColumns, FilterBuilderColumns(SelectedTemplateColumns, SelectedColumnSearch, ColumnViewMode));
    }

    private static IEnumerable<ReportColumnPickerItem> FilterBuilderColumns(
        IEnumerable<ReportColumnPickerItem> source,
        string search,
        string viewMode)
    {
        var modeFiltered = source.Where(item => MatchBuilderColumnMode(item, viewMode));

        if (string.IsNullOrWhiteSpace(search))
            return modeFiltered;

        var term = search.Trim();
        return modeFiltered.Where(item =>
            item.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
            || item.SourceField.Contains(term, StringComparison.OrdinalIgnoreCase)
            || item.CategoryLabel.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchBuilderColumnMode(ReportColumnPickerItem item, string mode)
    {
        var normalized = NormalizeBuilderKey(mode);
        return normalized switch
        {
            "DIMENSIONS" => IsBuilderDimensionColumn(item.Column) && !item.Column.VisibleInAdvancedMode,
            "METRICS" => IsBuilderMetricColumn(item.Column) && !item.Column.VisibleInAdvancedMode,
            "ADVANCED" => item.Column.VisibleInAdvancedMode,
            _ => !item.Column.VisibleInAdvancedMode
        };
    }

    private ReportTemplate BuildBuilderTemplate()
    {
        var sourceValue = Source.Trim();
        var rules = ConfiguredRules.Select(ToBuilderRuleDefinition).ToList();

        return new ReportTemplate
        {
            Id = SelectedTemplate?.Id ?? Guid.NewGuid(),
            Nombre = ReportName.Trim(),
            TipoReporte = sourceValue,
            ReporteOrigen = sourceValue,
            ColumnDesign = ColumnDesign,
            Columnas = SelectedTemplateColumns.Select(item => CloneColumn(item.Column)).ToList(),
            Ruth = _ruleSchemaService.BuildRulesJson(rules, _builderPreservedLegacyRuleNodes)
        };
    }

    private void ClearBuilderMetadata()
    {
        AvailableTemplateColumns.Clear();
        SelectedTemplateColumns.Clear();
        FilteredAvailableTemplateColumns.Clear();
        FilteredSelectedTemplateColumns.Clear();
        SelectedAvailableTemplateColumn = null;
        SelectedTemplateColumn = null;
        ColumnDesign = string.Empty;
        ColumnViewMode = "all";

        _builderFieldCatalog = Array.Empty<ReportFieldDefinition>();
        RuleDimensionChoices.Clear();
        RuleMetricChoices.Clear();
        RuleEditorDimension = string.Empty;
        RuleEditorMetric = string.Empty;

        OnPropertyChanged(nameof(BuilderColumnSummary));
        OnPropertyChanged(nameof(BuilderMetadataSummary));
        OnPropertyChanged(nameof(CanConfigureBuilderRules));
        OnPropertyChanged(nameof(BuilderColumnModelSummary));
        OnPropertyChanged(nameof(RuleMetricHint));
    }

    private void ClearBuilderRules()
    {
        ConfiguredRules.Clear();
        ConfiguredGeneralRules.Clear();
        ConfiguredTieredRules.Clear();
        PreservedLegacyRules.Clear();
        _builderPreservedLegacyRuleNodes.Clear();
        ClearRuleBuilderEditor();
        RefreshRuleBuckets();
        OnPropertyChanged(nameof(PreservedLegacyRulesSummary));
        OnPropertyChanged(nameof(IsPreservedLegacyRulesVisible));
    }

    private void RefreshRuleBuckets()
    {
        Replace(
            ConfiguredGeneralRules,
            ConfiguredRules.Where(rule => rule.Tiers.Count == 0));

        Replace(
            ConfiguredTieredRules,
            ConfiguredRules.Where(rule => rule.Tiers.Count > 0));

        OnPropertyChanged(nameof(BuilderRulesSummary));
        OnPropertyChanged(nameof(BuilderGeneralRulesSummary));
        OnPropertyChanged(nameof(BuilderTieredRulesSummary));
        OnPropertyChanged(nameof(RuleEditorTierSummary));
    }

    private void LoadBuilderStaticOptions()
    {
        Replace(ColumnViewModeChoices, new[]
        {
            new BuilderOption { Label = "Todo", Value = "all" },
            new BuilderOption { Label = "Dimensiones", Value = "dimensions" },
            new BuilderOption { Label = "Metricas", Value = "metrics" },
            new BuilderOption { Label = "Avanzado", Value = "advanced" }
        });

        Replace(RuleTypePresetChoices, new[]
        {
            new BuilderOption { Label = "Regla simple", Value = "simple" },
            new BuilderOption { Label = "Pago por descripcion", Value = "per_description" },
            new BuilderOption { Label = "Cumplir varias descripciones", Value = "multi_description" },
            new BuilderOption { Label = "Regla escalonada", Value = "tiered" }
        });

        Replace(RuleOperatorChoices, new[]
        {
            new BuilderOption { Label = "Mayor que", Value = ">" },
            new BuilderOption { Label = "Mayor o igual", Value = ">=" },
            new BuilderOption { Label = "Menor que", Value = "<" },
            new BuilderOption { Label = "Menor o igual", Value = "<=" },
            new BuilderOption { Label = "Igual a", Value = "=" },
            new BuilderOption { Label = "Distinto de", Value = "!=" }
        });

        Replace(RuleEvaluationChoices, new[]
        {
            new BuilderOption { Label = "Marcar filas que cumplan", Value = "mark_matches" },
            new BuilderOption { Label = "Contar elementos que cumplen", Value = "count_matches" },
            new BuilderOption { Label = "Validar minimo de elementos", Value = "threshold_by_count" },
            new BuilderOption { Label = "Validar total acumulado", Value = "threshold_by_total" }
        });

        Replace(RuleActionChoices, new[]
        {
            new BuilderOption { Label = "Sin resultado adicional", Value = "none" },
            new BuilderOption { Label = "Marca visual", Value = "mark" },
            new BuilderOption { Label = "Premio / bono", Value = "reward" },
            new BuilderOption { Label = "Multa / castigo", Value = "penalty" },
            new BuilderOption { Label = "Estado aprobado", Value = "approved" },
            new BuilderOption { Label = "Estado no aprobado", Value = "rejected" },
            new BuilderOption { Label = "Definir estado", Value = "set_status" },
            new BuilderOption { Label = "Bandera", Value = "flag" }
        });
    }

    private void EnsureBuilderRuleSelections()
    {
        if (!RuleDimensionChoices.Any(option => IsSameBuilderKey(option.Value, RuleEditorDimension)))
        {
            RuleEditorDimension = RuleDimensionChoices
                .FirstOrDefault(option => NormalizeBuilderKey(option.DisplayName).Contains("DESCRIPCION", StringComparison.OrdinalIgnoreCase))?.Value
                ?? RuleDimensionChoices.FirstOrDefault()?.Value
                ?? string.Empty;
        }

        if (!RuleMetricChoices.Any(option => IsSameBuilderKey(option.Value, RuleEditorMetric)))
        {
            RuleEditorMetric = RuleMetricChoices
                .FirstOrDefault(option => NormalizeBuilderKey(option.DisplayName).Contains("CORDOBAS", StringComparison.OrdinalIgnoreCase))?.Value
                ?? RuleMetricChoices.FirstOrDefault()?.Value
                ?? string.Empty;
        }
    }

    private void UpdateBuilderEditorVisibility()
    {
        OnPropertyChanged(nameof(IsRuleEditorTargetVisible));
        OnPropertyChanged(nameof(IsRuleEditorTierSectionVisible));
        OnPropertyChanged(nameof(IsRuleEditorSuccessSectionVisible));
        OnPropertyChanged(nameof(RuleEditorFailureSectionTitle));
        OnPropertyChanged(nameof(IsRuleEditorSuccessAmountVisible));
        OnPropertyChanged(nameof(IsRuleEditorSuccessValueVisible));
        OnPropertyChanged(nameof(IsRuleEditorFailureAmountVisible));
        OnPropertyChanged(nameof(IsRuleEditorFailureValueVisible));
        OnPropertyChanged(nameof(IsRuleEditorTierAmountVisible));
        OnPropertyChanged(nameof(IsRuleEditorTierValueVisible));
        OnPropertyChanged(nameof(RuleEditorTypePresetHelp));
        OnPropertyChanged(nameof(IsSimpleTypePresetSelected));
        OnPropertyChanged(nameof(IsPerDescriptionTypePresetSelected));
        OnPropertyChanged(nameof(IsMultiDescriptionTypePresetSelected));
        OnPropertyChanged(nameof(IsTieredTypePresetSelected));
        OnPropertyChanged(nameof(IsRuleTypeSimpleVisible));
        OnPropertyChanged(nameof(IsRuleTypePerDescriptionVisible));
        OnPropertyChanged(nameof(IsRuleTypeMultiDescriptionVisible));
        OnPropertyChanged(nameof(IsRuleTypeTieredVisible));
        OnPropertyChanged(nameof(IsRuleTierToggleVisible));
        OnPropertyChanged(nameof(IsRuleSuccessSectionByPresetVisible));
        OnPropertyChanged(nameof(IsRuleTierSectionByPresetVisible));
        OnPropertyChanged(nameof(IsRuleSuccessCardVisible));
        OnPropertyChanged(nameof(IsRuleFailureCardVisible));
        OnPropertyChanged(nameof(RuleEditorLiveSummary));
        OnPropertyChanged(nameof(IsRuleEditorNameValidationVisible));
        OnPropertyChanged(nameof(IsRuleEditorMetricValidationVisible));
        OnPropertyChanged(nameof(IsRuleEditorConditionValidationVisible));
        OnPropertyChanged(nameof(IsRuleEditorRewardValidationVisible));
        OnPropertyChanged(nameof(IsPerDescriptionDuplicateValidationVisible));
        OnPropertyChanged(nameof(CanSaveRuleEditor));
        OnPropertyChanged(nameof(CanDeleteSelectedRule));
        UpdateRuleEditorSoftValidations();
    }

    private static string NormalizeRuleTypePresetKey(string? value)
    {
        var key = (value ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "per_description" => "per_description",
            "multi_description" => "multi_description",
            "tiered" => "tiered",
            _ => "simple"
        };
    }

    private static ReportFieldOption ToBuilderFieldOption(ReportFieldDefinition field)
    {
        var displayName = string.IsNullOrWhiteSpace(field.SourceField)
            ? field.DisplayName
            : field.SourceField;

        return new ReportFieldOption
        {
            Value = string.IsNullOrWhiteSpace(field.SourceField) ? field.Key : field.SourceField,
            DisplayName = displayName,
            SourceField = field.SourceField,
            CategoryLabel = field.IsDimension ? "Dimension" : field.IsMeasure ? "Metrica" : "Calculada",
            DataType = field.DataType,
            CanonicalKey = field.CanonicalKey,
            IsAdvanced = field.VisibleInAdvancedMode
        };
    }

    private static IReadOnlyList<ReportFieldDefinition> BuildBuilderFieldCatalog(IEnumerable<ReportColumnDefinition> columns)
    {
        return columns
            .Select((column, index) => new ReportFieldDefinition
            {
                Key = string.IsNullOrWhiteSpace(column.Key) ? NormalizeBuilderKey(column.SourceField) : column.Key,
                DisplayName = string.IsNullOrWhiteSpace(column.DisplayName) ? column.SourceField : column.DisplayName,
                SourceTable = column.SourceTable,
                SourceField = string.IsNullOrWhiteSpace(column.SourceField) ? column.Key : column.SourceField,
                SourceType = column.SourceType,
                DataType = column.DataType,
                IsMeasure = column.IsMeasure || column.SourceType == ReportColumnSourceType.Measure,
                IsDimension = column.IsDimension || column.SourceType == ReportColumnSourceType.Dimension,
                IsCalculated = column.IsCalculated || column.SourceType == ReportColumnSourceType.Calculated,
                DefaultFormat = column.DefaultFormat ?? column.FormatString ?? string.Empty,
                AllowSorting = column.AllowSorting,
                AllowFiltering = column.AllowFiltering,
                AllowRules = column.AllowRules,
                DefaultOrder = index,
                VisibleInColumnSelector = column.VisibleInColumnSelector,
                VisibleInRuleScopeSelector = column.IsDimension || column.SourceType == ReportColumnSourceType.Dimension,
                VisibleInRuleMetricSelector = column.IsMeasure || column.SourceType == ReportColumnSourceType.Measure,
                VisibleInAdvancedMode = column.VisibleInAdvancedMode,
                UsageCategory = ResolveBuilderUsageCategory(column),
                CanonicalKey = string.IsNullOrWhiteSpace(column.CatalogCanonicalKey)
                    ? NormalizeBuilderIdentity(column.SourceField)
                    : column.CatalogCanonicalKey
            })
            .ToList();
    }

    private static ReportFieldUsageCategory ResolveBuilderUsageCategory(ReportColumnDefinition column)
    {
        if (!string.IsNullOrWhiteSpace(column.CatalogCategory)
            && Enum.TryParse<ReportFieldUsageCategory>(column.CatalogCategory, true, out var category))
        {
            return category;
        }

        if (column.IsDimension || column.SourceType == ReportColumnSourceType.Dimension)
            return ReportFieldUsageCategory.Dimension;

        if (column.IsMeasure || column.SourceType == ReportColumnSourceType.Measure)
            return ReportFieldUsageCategory.Metric;

        return column.VisibleInAdvancedMode
            ? ReportFieldUsageCategory.Hidden
            : ReportFieldUsageCategory.Unknown;
    }

    private static string ResolveBuilderColumnValue(ReportColumnDefinition column)
        => string.IsNullOrWhiteSpace(column.SourceField) ? column.Key : column.SourceField;

    private static bool TryBuildBuilderAction(string type, string amountText, string currency, string valueText, out GuidedRuleActionUiModel action, out string error)
    {
        action = new GuidedRuleActionUiModel
        {
            Type = string.IsNullOrWhiteSpace(type) ? "none" : type.Trim(),
            Currency = string.IsNullOrWhiteSpace(currency) ? "NIO" : currency.Trim(),
            Value = string.Empty
        };

        error = string.Empty;
        if (string.Equals(action.Type, "none", StringComparison.OrdinalIgnoreCase))
            return true;

        if (RequiresBuilderAmount(action.Type))
        {
            if (!TryParseDecimal(amountText, out var amount))
            {
                error = "El monto configurado para la accion debe ser numerico.";
                return false;
            }

            action.Amount = amount;
        }

        if (RequiresBuilderText(action.Type))
            action.Value = string.IsNullOrWhiteSpace(valueText) ? DefaultBuilderActionValue(action.Type) : valueText.Trim();
        else
            action.Value = action.Type switch { "approved" => "Aprobado", "rejected" => "No aprobado", _ => string.Empty };

        return true;
    }

    private static GuidedRuleUiModel ToBuilderRuleUi(CustomReportTemplateRuleDefinition definition)
        => new()
        {
            Id = definition.Id,
            Name = definition.Name,
            Dimension = definition.Dimension,
            DimensionLabel = definition.DimensionLabel,
            Metric = definition.Metric,
            MetricLabel = definition.MetricLabel,
            Operator = definition.Operator,
            Value = definition.Value,
            EvaluationType = ToBuilderRuleValue(definition.EvaluationType),
            Comparison = definition.Comparison,
            Target = definition.Target,
            SuccessAction = ToBuilderRuleActionUi(definition.SuccessAction),
            FailureAction = ToBuilderRuleActionUi(definition.FailureAction),
            Tiers = new ObservableCollection<GuidedRuleTierUiModel>(definition.Tiers.Select(ToBuilderRuleTierUi))
        };

    private static CustomReportTemplateRuleDefinition ToBuilderRuleDefinition(GuidedRuleUiModel rule)
        => new()
        {
            Id = rule.Id,
            Name = rule.Name,
            Dimension = rule.Dimension,
            DimensionLabel = rule.DimensionLabel,
            Metric = rule.Metric,
            MetricLabel = rule.MetricLabel,
            Operator = NormalizeBuilderOperator(rule.Operator),
            Value = rule.Value,
            EvaluationType = ParseBuilderEvaluationType(rule.EvaluationType),
            Comparison = NormalizeBuilderOperator(rule.Comparison, ">="),
            Target = rule.Target,
            SuccessAction = ToBuilderRuleActionDefinition(rule.SuccessAction),
            FailureAction = ToBuilderRuleActionDefinition(rule.FailureAction),
            Tiers = rule.Tiers.Select(ToBuilderRuleTierDefinition).ToList()
        };

    private static GuidedRuleTierUiModel ToBuilderRuleTierUi(CustomReportRuleTierDefinition tier)
        => new()
        {
            Name = tier.Name,
            Operator = NormalizeBuilderOperator(tier.Condition.Operator, ">="),
            Value = tier.Condition.Value,
            Result = ToBuilderRuleActionUi(tier.Result)
        };

    private static CustomReportRuleTierDefinition ToBuilderRuleTierDefinition(GuidedRuleTierUiModel tier)
        => new()
        {
            Name = tier.Name,
            Condition = new CustomReportRuleTierConditionDefinition
            {
                Operator = NormalizeBuilderOperator(tier.Operator, ">="),
                Value = tier.Value
            },
            Result = ToBuilderRuleActionDefinition(tier.Result)
        };

    private static GuidedRuleActionUiModel ToBuilderRuleActionUi(CustomReportRuleActionDefinition action)
        => new()
        {
            Type = ToBuilderRuleValue(action.Type),
            Amount = action.Amount,
            Currency = action.Currency,
            Value = action.Value
        };

    private static CustomReportRuleActionDefinition ToBuilderRuleActionDefinition(GuidedRuleActionUiModel action)
        => new()
        {
            Type = ParseBuilderActionType(action.Type),
            Amount = action.Amount,
            Currency = action.Currency,
            Value = action.Value
        };

    private static GuidedRuleTierUiModel CloneBuilderTier(GuidedRuleTierUiModel tier)
        => new()
        {
            Name = tier.Name,
            Operator = tier.Operator,
            Value = tier.Value,
            Result = new GuidedRuleActionUiModel
            {
                Type = tier.Result.Type,
                Amount = tier.Result.Amount,
                Currency = tier.Result.Currency,
                Value = tier.Result.Value
            }
        };

    private void UpdateRuleEditorSoftValidations()
    {
        RuleEditorNameValidationMessage = string.IsNullOrWhiteSpace(RuleEditorName)
            ? "Escribe un nombre para identificar esta regla."
            : string.Empty;

        var selectedMetric = IsMultiDescriptionTypePresetSelected ? MultiDescriptionMetric : RuleEditorMetric;
        RuleEditorMetricValidationMessage = string.IsNullOrWhiteSpace(selectedMetric)
            ? "Selecciona el indicador que quieres evaluar."
            : string.Empty;

        var conditionValue = IsMultiDescriptionTypePresetSelected ? MultiDescriptionTargetPerDescription : RuleEditorValue;
        RuleEditorConditionValidationMessage = string.IsNullOrWhiteSpace(conditionValue)
            ? "Completa la meta para saber cuando se cumple la condicion."
            : string.Empty;

        RuleEditorRewardValidationMessage = BuildRewardValidationMessage();
        PerDescriptionDuplicateValidationMessage = BuildPerDescriptionDuplicateValidationMessage();
    }

    private string BuildRewardValidationMessage()
    {
        if (IsPerDescriptionTypePresetSelected)
        {
            var hasAmount = PerDescriptionRuleLines.Any(line => !string.IsNullOrWhiteSpace(line.Amount));
            return hasAmount
                ? string.Empty
                : "Agrega al menos un monto por descripcion para definir el premio.";
        }

        if (IsMultiDescriptionTypePresetSelected)
        {
            return string.IsNullOrWhiteSpace(MultiDescriptionSingleReward)
                ? "Completa el premio unico para esta regla."
                : string.Empty;
        }

        if (IsTieredTypePresetSelected)
            return string.Empty;

        var requiresAmount = RequiresBuilderAmount(RuleEditorSuccessActionType);
        if (!requiresAmount)
            return string.Empty;

        return string.IsNullOrWhiteSpace(RuleEditorSuccessAmount)
            ? "Completa el monto del premio para continuar."
            : string.Empty;
    }

    private string BuildPerDescriptionDuplicateValidationMessage()
    {
        if (!IsPerDescriptionTypePresetSelected)
            return string.Empty;

        var duplicates = PerDescriptionRuleLines
            .Select(line => line.Description?.Trim() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => NormalizeBuilderKey(value))
            .Where(group => group.Count() > 1)
            .ToList();

        return duplicates.Count > 0
            ? "Hay descripciones repetidas. Deja solo una fila por descripcion."
            : string.Empty;
    }

    private void EnsurePerDescriptionRuleLines()
    {
        EnsurePerDescriptionLineSummarySubscriptions();
        if (PerDescriptionRuleLines.Count == 0)
            PerDescriptionRuleLines.Add(new DescriptionAmountRuleLineUiModel());
    }

    private void EnsurePerDescriptionLineSummarySubscriptions()
    {
        if (_perDescriptionLinesSummaryHooked)
            return;

        _perDescriptionLinesSummaryHooked = true;
        PerDescriptionRuleLines.CollectionChanged += OnPerDescriptionRuleLinesCollectionChanged;

        foreach (var line in PerDescriptionRuleLines)
            line.PropertyChanged += OnPerDescriptionRuleLinePropertyChanged;
    }

    private void OnPerDescriptionRuleLinesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems.OfType<DescriptionAmountRuleLineUiModel>())
                item.PropertyChanged -= OnPerDescriptionRuleLinePropertyChanged;
        }

        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems.OfType<DescriptionAmountRuleLineUiModel>())
                item.PropertyChanged += OnPerDescriptionRuleLinePropertyChanged;
        }

        OnPropertyChanged(nameof(RuleEditorLiveSummary));
        UpdateRuleEditorSoftValidations();
    }

    private void OnPerDescriptionRuleLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DescriptionAmountRuleLineUiModel.Description) or nameof(DescriptionAmountRuleLineUiModel.Amount))
        {
            OnPropertyChanged(nameof(RuleEditorLiveSummary));
            UpdateRuleEditorSoftValidations();
        }
    }

    private List<ReportColumnPickerItem> GetCheckedAvailableColumns()
    {
        var items = AvailableTemplateColumns.Where(item => item.IsChecked).ToList();
        if (items.Count == 0 && SelectedAvailableTemplateColumn != null)
            items.Add(SelectedAvailableTemplateColumn);

        return items;
    }

    private List<ReportColumnPickerItem> GetCheckedSelectedColumns()
    {
        var items = SelectedTemplateColumns.Where(item => item.IsChecked).ToList();
        if (items.Count == 0 && SelectedTemplateColumn != null)
            items.Add(SelectedTemplateColumn);

        return items;
    }

    private static ReportColumnPickerItem? TakeBuilderColumnMatch(List<ReportColumnPickerItem> remaining, string key)
    {
        var match = remaining.FirstOrDefault(item =>
            IsSameBuilderKey(item.Column.SourceField, key)
            || IsSameBuilderKey(item.Column.Key, key)
            || IsSameBuilderKey(item.Column.DisplayName, key));

        if (match != null)
            remaining.Remove(match);

        return match;
    }

    private static ReportFieldOption? FindBuilderFieldOption(IEnumerable<ReportFieldOption> source, string value)
        => source.FirstOrDefault(option => IsSameBuilderKey(option.Value, value));

    private string BuildBuilderRuleName(ReportFieldOption? dimension, ReportFieldOption? metric, decimal threshold)
        => RuleEditorUseTiers
            ? $"{dimension?.DisplayName ?? "Detalle"}: {metric?.DisplayName ?? "Metrica"} escalonado"
            : $"{dimension?.DisplayName ?? "Detalle"}: {metric?.DisplayName ?? "Metrica"} > {threshold.ToString("0.##", CultureInfo.InvariantCulture)}";

    private static int GetBuilderColumnCategoryOrder(ReportColumnDefinition column)
    {
        if (IsBuilderDimensionColumn(column))
            return 0;

        if (IsBuilderMetricColumn(column))
            return 1;

        return 2;
    }

    private static bool IsBuilderDimensionColumn(ReportColumnDefinition column)
    {
        if (!string.IsNullOrWhiteSpace(column.CatalogCategory)
            && string.Equals(column.CatalogCategory, ReportFieldUsageCategory.Dimension.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return column.IsDimension || column.SourceType == ReportColumnSourceType.Dimension;
    }

    private static bool IsBuilderMetricColumn(ReportColumnDefinition column)
    {
        if (!string.IsNullOrWhiteSpace(column.CatalogCategory)
            && string.Equals(column.CatalogCategory, ReportFieldUsageCategory.Metric.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return column.IsMeasure
               || column.SourceType == ReportColumnSourceType.Measure
               || (column.SourceType == ReportColumnSourceType.Calculated && column.DataType is ReportFieldDataType.Integer or ReportFieldDataType.Decimal);
    }

    private static bool RequiresBuilderTarget(string value) => NormalizeBuilderKey(value) is "THRESHOLDBYCOUNT" or "THRESHOLDBYTOTAL";
    private static bool RequiresBuilderAmount(string value) => NormalizeBuilderKey(value) is "REWARD" or "PENALTY";
    private static bool RequiresBuilderText(string value) => NormalizeBuilderKey(value) is "SETSTATUS" or "FLAG";
    private static string DefaultBuilderActionValue(string value) => NormalizeBuilderKey(value) == "FLAG" ? "Marcado" : "Revision manual";
    private static bool IsSameBuilderKey(string? left, string? right) => string.Equals(NormalizeBuilderIdentity(left), NormalizeBuilderIdentity(right), StringComparison.OrdinalIgnoreCase);
    private static string NormalizeBuilderOperator(string? value, string fallback = ">")
    {
        var result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return result is ">" or ">=" or "<" or "<=" or "=" or "!=" ? result : fallback;
    }

    private static string NormalizeBuilderIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var prepared = value.Trim();
        var open = prepared.LastIndexOf('[');
        var close = prepared.LastIndexOf(']');
        if (open >= 0 && close > open)
            prepared = prepared.Substring(open + 1, close - open - 1);

        // Evita colisiones entre metricas como %MD_COB y MD_COB.
        prepared = prepared.Replace("%", "PCT", StringComparison.Ordinal);

        return new string(prepared
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '+')
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static string NormalizeBuilderKey(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : new string(value.Trim().Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string ToBuilderRuleValue(CustomReportRuleEvaluationType value) => value switch
    {
        CustomReportRuleEvaluationType.CountMatches => "count_matches",
        CustomReportRuleEvaluationType.ThresholdByCount => "threshold_by_count",
        CustomReportRuleEvaluationType.ThresholdByTotal => "threshold_by_total",
        _ => "mark_matches"
    };

    private static string ToBuilderRuleValue(CustomReportRuleActionType value) => value switch
    {
        CustomReportRuleActionType.Mark => "mark",
        CustomReportRuleActionType.Reward => "reward",
        CustomReportRuleActionType.Penalty => "penalty",
        CustomReportRuleActionType.Approved => "approved",
        CustomReportRuleActionType.Rejected => "rejected",
        CustomReportRuleActionType.SetStatus => "set_status",
        CustomReportRuleActionType.Flag => "flag",
        _ => "none"
    };

    private static CustomReportRuleEvaluationType ParseBuilderEvaluationType(string value) => NormalizeBuilderKey(value) switch
    {
        "COUNTMATCHES" => CustomReportRuleEvaluationType.CountMatches,
        "THRESHOLDBYCOUNT" => CustomReportRuleEvaluationType.ThresholdByCount,
        "THRESHOLDBYTOTAL" => CustomReportRuleEvaluationType.ThresholdByTotal,
        _ => CustomReportRuleEvaluationType.MarkMatches
    };

    private static CustomReportRuleActionType ParseBuilderActionType(string value) => NormalizeBuilderKey(value) switch
    {
        "MARK" => CustomReportRuleActionType.Mark,
        "REWARD" => CustomReportRuleActionType.Reward,
        "PENALTY" => CustomReportRuleActionType.Penalty,
        "APPROVED" => CustomReportRuleActionType.Approved,
        "REJECTED" => CustomReportRuleActionType.Rejected,
        "SETSTATUS" => CustomReportRuleActionType.SetStatus,
        "FLAG" => CustomReportRuleActionType.Flag,
        _ => CustomReportRuleActionType.None
    };
}
