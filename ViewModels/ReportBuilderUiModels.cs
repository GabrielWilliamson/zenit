using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Zenit.Models.CustomReports;
using Microsoft.UI.Xaml;

namespace Zenit.ViewModels;

public sealed partial class ReportSelectableOption : ObservableObject
{
    public ReportSelectableOption(string displayName, string value)
    {
        DisplayName = displayName;
        Value = value;
    }

    public string DisplayName { get; }
    public string Value { get; }

    [ObservableProperty]
    private bool isSelected;
}

public sealed class SourceModeOption
{
    public required string DisplayName { get; init; }
    public required ReportSourceMode Mode { get; init; }
}

public sealed class BuilderOption
{
    public required string Label { get; init; }
    public required string Value { get; init; }
    public string Description { get; init; } = string.Empty;

    public override string ToString() => Label;
}

public sealed class ReportFieldOption
{
    public required string Value { get; init; }
    public required string DisplayName { get; init; }
    public string SourceField { get; init; } = string.Empty;
    public string CategoryLabel { get; init; } = string.Empty;
    public ReportFieldDataType DataType { get; init; } = ReportFieldDataType.Unknown;
    public string CanonicalKey { get; init; } = string.Empty;
    public bool IsAdvanced { get; init; }

    public string Hint => string.IsNullOrWhiteSpace(SourceField)
        ? CategoryLabel
        : $"{CategoryLabel} - {SourceField}";

    public override string ToString() => DisplayName;
}

public sealed partial class ReportColumnPickerItem : ObservableObject
{
    public ReportColumnPickerItem(ReportColumnDefinition column)
    {
        Column = column;
    }

    public ReportColumnDefinition Column { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Column.DisplayName)
        ? Column.SourceField
        : Column.DisplayName;

    public string SourceField => string.IsNullOrWhiteSpace(Column.SourceField)
        ? Column.Key
        : Column.SourceField;

    public string CategoryLabel => Column.IsDimension || Column.SourceType == ReportColumnSourceType.Dimension
        ? "Dimension"
        : Column.IsMeasure || Column.SourceType == ReportColumnSourceType.Measure
            ? "Metrica"
            : Column.IsCalculated || Column.SourceType == ReportColumnSourceType.Calculated
                ? "Calculada"
                : ResolveCategoryFromCatalog();

    public bool IsAdvanced => Column.VisibleInAdvancedMode;

    public string Subtitle => string.IsNullOrWhiteSpace(SourceField)
        ? CategoryLabel
        : $"{CategoryLabel} - {SourceField}";

    [ObservableProperty]
    private bool isChecked;

    private string ResolveCategoryFromCatalog()
    {
        return Column.CatalogCategory?.ToUpperInvariant() switch
        {
            "DIMENSION" => "Dimension",
            "METRIC" => "Metrica",
            "HIDDEN" => "Avanzada",
            _ => "Campo"
        };
    }

    public override string ToString() => $"{DisplayName} ({CategoryLabel})";
}

public sealed class GuidedRuleActionUiModel
{
    public string Type { get; set; } = "none";
    public decimal? Amount { get; set; }
    public string Currency { get; set; } = "NIO";
    public string Value { get; set; } = string.Empty;

    public string DisplayText
    {
        get
        {
            return Type switch
            {
                "mark" => "Marca visual",
                "reward" when Amount.HasValue => $"Premio {Currency} {Amount.Value.ToString("0.##", CultureInfo.InvariantCulture)}",
                "penalty" when Amount.HasValue => $"Multa {Currency} {Amount.Value.ToString("0.##", CultureInfo.InvariantCulture)}",
                "approved" => "Estado aprobado",
                "rejected" => "Estado no aprobado",
                "set_status" when !string.IsNullOrWhiteSpace(Value) => $"Estado: {Value}",
                "flag" when !string.IsNullOrWhiteSpace(Value) => $"Bandera: {Value}",
                "flag" => "Bandera",
                _ => "Sin accion"
            };
        }
    }
}

public sealed class GuidedRuleTierUiModel
{
    public string Name { get; set; } = string.Empty;
    public string Operator { get; set; } = ">=";
    public decimal Value { get; set; }
    public GuidedRuleActionUiModel Result { get; set; } = new();

    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? $"Nivel {Operator} {Value.ToString("0.##", CultureInfo.InvariantCulture)}"
        : Name;

    public string Summary => $"{Operator} {Value.ToString("0.##", CultureInfo.InvariantCulture)} -> {Result.DisplayText}";
}

public sealed class GuidedRuleUiModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Dimension { get; set; } = string.Empty;
    public string DimensionLabel { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public string MetricLabel { get; set; } = string.Empty;
    public string Operator { get; set; } = ">=";
    public decimal Value { get; set; }
    public string EvaluationType { get; set; } = "mark_matches";
    public string Comparison { get; set; } = ">=";
    public decimal? Target { get; set; }
    public GuidedRuleActionUiModel SuccessAction { get; set; } = new();
    public GuidedRuleActionUiModel FailureAction { get; set; } = new();
    public ObservableCollection<GuidedRuleTierUiModel> Tiers { get; set; } = new();

    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? $"{MetricLabel} {Operator} {Value.ToString("0.##", CultureInfo.InvariantCulture)}"
        : Name;

    public bool UsesTiers => Tiers.Count > 0;

    public string ConditionSummary
        => $"{SafeLabel(DimensionLabel, "Detalle")} -> {SafeLabel(MetricLabel, "Metrica")} {Operator} {Value.ToString("0.##", CultureInfo.InvariantCulture)}";

    public string EvaluationSummary
        => UsesTiers
            ? $"{ResolveEvaluationLabel()} con {Tiers.Count} nivel(es) escalonados."
            : EvaluationType switch
        {
            "count_matches" => $"Cuenta cuantos elementos de {SafeLabel(DimensionLabel, "detalle")} cumplen la condicion.",
            "threshold_by_count" => $"Valida cantidad de elementos que cumplen: {Comparison} {FormatNullable(Target)}.",
            "threshold_by_total" => $"Valida total acumulado de la metrica: {Comparison} {FormatNullable(Target)}.",
            _ => "Marca las filas que cumplan la condicion."
        };

    public string TierSummary => UsesTiers
        ? string.Join(" | ", Tiers.Select(tier => tier.Summary))
        : string.Empty;

    public string OutcomeSummary
    {
        get
        {
            if (UsesTiers)
            {
                var failure = FailureAction.Type == "none"
                    ? "Sin resultado si no alcanza nivel."
                    : $"Si no alcanza nivel: {FailureAction.DisplayText}";
                return $"{TierSummary} | {failure}";
            }

            var success = $"Si cumple: {SuccessAction.DisplayText}";
            if (FailureAction.Type == "none")
                return success;

            return $"{success} | Si no cumple: {FailureAction.DisplayText}";
        }
    }

    private string ResolveEvaluationLabel() => EvaluationType switch
    {
        "count_matches" => "Cuenta elementos que cumplen",
        "threshold_by_count" => "Valida cantidad de elementos",
        "threshold_by_total" => "Valida total acumulado",
        _ => "Marca filas que cumplen"
    };

    private static string SafeLabel(string value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string FormatNullable(decimal? value)
        => value.HasValue
            ? value.Value.ToString("0.##", CultureInfo.InvariantCulture)
            : "-";

    public override string ToString() => DisplayName;
}

public sealed class LegacyRuleUiModel
{
    public string Name { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

public sealed class TemplateGlobalRuleUiModel
{
    public string Line { get; set; } = string.Empty;
    public string Scope { get; set; } = "descripcion";
    public string Currency { get; set; } = "NIO";
    public string RoutesText { get; set; } = "*";
    public string Metric { get; set; } = "volume";
    public string Operator { get; set; } = ">=";
    public decimal ThresholdValue { get; set; }
    public decimal? RewardAmount { get; set; }
    public string OverrideRoutesText { get; set; } = string.Empty;
    public decimal? OverrideRewardAmount { get; set; }
    public string SuccessColor { get; set; } = "green";
    public string SuccessStatus { get; set; } = "cumple";
    public string FailColor { get; set; } = "red";
    public string FailStatus { get; set; } = "no_cumple";

    public string DisplayName => string.IsNullOrWhiteSpace(Line)
        ? "Regla global"
        : Line;

    public string Summary
    {
        get
        {
            var baseCondition = $"{Metric} {Operator} {ThresholdValue.ToString("0.##", CultureInfo.InvariantCulture)}";
            var reward = RewardAmount.HasValue
                ? $" -> premio {Currency} {RewardAmount.Value.ToString("0.##", CultureInfo.InvariantCulture)}"
                : string.Empty;
            return $"{Scope}: {baseCondition}{reward}";
        }
    }
}

public sealed class TemplateScaleRuleUiModel
{
    public string Line { get; set; } = string.Empty;
    public string Currency { get; set; } = "NIO";
    public string Metric { get; set; } = "volume";
    public ObservableCollection<TemplateScaleTierUiModel> Tiers { get; set; } = new();

    public string DisplayName => string.IsNullOrWhiteSpace(Line)
        ? "Regla escalonada"
        : Line;

    public string Summary => $"{Metric} con {Tiers.Count} tramo(s)";
}

public sealed class TemplateScaleTierUiModel
{
    public decimal Min { get; set; }
    public decimal? Max { get; set; }
    public decimal DefaultReward { get; set; }
    public ObservableCollection<TemplateScaleTierOverrideUiModel> Overrides { get; set; } = new();

    public string DisplayName
    {
        get
        {
            var maxText = Max.HasValue
                ? Max.Value.ToString("0.##", CultureInfo.InvariantCulture)
                : "sin tope";
            return $"{Min.ToString("0.##", CultureInfo.InvariantCulture)} - {maxText}";
        }
    }
}

public sealed class TemplateScaleTierOverrideUiModel
{
    public string RoutesText { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(RoutesText)
        ? Amount.ToString("0.##", CultureInfo.InvariantCulture)
        : $"{RoutesText}: {Amount.ToString("0.##", CultureInfo.InvariantCulture)}";
}

public sealed partial class DescriptionAmountRuleLineUiModel : ObservableObject
{
    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private string amount = string.Empty;

    public Visibility AmountValidationVisibility => string.IsNullOrWhiteSpace(Amount)
        ? Visibility.Visible
        : Visibility.Collapsed;

    partial void OnAmountChanged(string value)
    {
        OnPropertyChanged(nameof(AmountValidationVisibility));
    }
}

public sealed partial class MultiDescriptionRouteExceptionUiModel : ObservableObject
{
    [ObservableProperty]
    private string route = string.Empty;

    [ObservableProperty]
    private string requiredCount = string.Empty;

    [ObservableProperty]
    private string reward = string.Empty;
}
