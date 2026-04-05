using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zenit.Models.CustomReports;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReportSourceMode
{
    ExistingReport,
    FreeCubeQuery
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReportFieldDataType
{
    Unknown,
    Text,
    Integer,
    Decimal,
    Date,
    Boolean
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReportSortDirection
{
    Asc,
    Desc
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReportFormatType
{
    Auto,
    Integer,
    Decimal,
    Percentage,
    Currency,
    Accounting,
    Thousands,
    LocalThousands,
    Text
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReportAlignment
{
    Auto,
    Left,
    Center,
    Right
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReportRuleType
{
    Visual,
    Calculation,
    State,
    PremioAfectacion
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReportRuleScope
{
    Cell,
    Column,
    Row,
    Result
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReportLogicalOperator
{
    And,
    Or
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RuleValueSourceType
{
    Constant,
    Field
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RuleOperatorType
{
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual,
    Equal,
    NotEqual,
    Between,
    IsEmpty,
    IsNotEmpty,
    Contains,
    NotContains
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RuleActionType
{
    ChangeCellColor,
    ChangeTextColor,
    ShowIcon,
    ShowCheck,
    ShowText,
    CalculatePremio,
    CalculateAfectacion,
    SetValue,
    SetSemaforo,
    SetEstado,
    SetObservacion
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CustomReportRuleEvaluationType
{
    MarkMatches,
    CountMatches,
    ThresholdByCount,
    ThresholdByTotal
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CustomReportRuleActionType
{
    None,
    Mark,
    Reward,
    Penalty,
    Approved,
    Rejected,
    SetStatus,
    Flag
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GuidedRuleBusinessType
{
    None,
    PerDescriptionReward,
    QualifiedDescriptionCount
}

// Define el origen funcional de una columna para soportar escenarios dinamicos.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReportColumnSourceType
{
    Unknown,
    Dimension,
    Measure,
    Calculated,
    RuleOutput
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReportFieldUsageCategory
{
    Unknown,
    Dimension,
    Metric,
    Hidden
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReportRuleOutputType
{
    Visual,
    Numeric,
    Text,
    Flag
}

public sealed class ReportTypeDefinition
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    public override string ToString() => DisplayName;
}

public sealed class ReportSourceDefinition
{
    public string ReporteNombre { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class ReportTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nombre { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Categoria { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public ReportSourceMode SourceMode { get; set; } = ReportSourceMode.ExistingReport;
    public string TipoReporte { get; set; } = string.Empty;
    public string ReporteOrigenReal { get; set; } = string.Empty;
    public string ReporteOrigen
    {
        get => string.IsNullOrWhiteSpace(ReporteOrigenReal) ? TipoReporte : ReporteOrigenReal;
        set
        {
            TipoReporte = value ?? string.Empty;
            ReporteOrigenReal = value ?? string.Empty;
        }
    }
    public string ColumnDesign { get; set; } = string.Empty;
    public List<JsonElement> Ruth { get; set; } = new();
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
    public int Mes { get; set; }
    public int Anio { get; set; }
    public List<string> Vendedores { get; set; } = new();
    public List<ReportFilterDefinition> Filtros { get; set; } = new();
    public List<ReportColumnDefinition> Columnas { get; set; } = new();
    public List<ReportSortDefinition> SortDefinitions { get; set; } = new();
    public List<ReportFormatDefinition> FormatDefinitions { get; set; } = new();
    public List<ReportRuleDefinition> Reglas { get; set; } = new();
    public List<AggregationRuleDefinition> AggregationRules { get; set; } = new();
    public DateTime FechaCreacionUtc { get; set; } = DateTime.UtcNow;
    public DateTime FechaActualizacionUtc { get; set; } = DateTime.UtcNow;
    public DateTime FechaModificacionUtc
    {
        get => FechaActualizacionUtc;
        set => FechaActualizacionUtc = value;
    }

    [JsonIgnore]
    public List<ReportColumnDefinition> SelectedFields
    {
        get => Columnas;
        set => Columnas = value ?? new List<ReportColumnDefinition>();
    }

    [JsonIgnore]
    public List<ReportRuleDefinition> RuleDefinitions
    {
        get => Reglas;
        set => Reglas = value ?? new List<ReportRuleDefinition>();
    }

    public override string ToString() => string.IsNullOrWhiteSpace(Nombre) ? "Plantilla" : Nombre;
}

public sealed class ReportFilterDefinition
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Value { get; set; }
    public List<string> Values { get; set; } = new();
    public bool IsRequired { get; set; }
    public bool AllowMultiple { get; set; } = true;
}

public sealed class ReportColumnDefinition
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Alias
    {
        get => DisplayName;
        set => DisplayName = string.IsNullOrWhiteSpace(value) ? Key : value.Trim();
    }
    public string SourceTable { get; set; } = string.Empty;
    public string SourceField { get; set; } = string.Empty;
    public ReportFieldDataType DataType { get; set; } = ReportFieldDataType.Unknown;
    public ReportColumnSourceType SourceType { get; set; } = ReportColumnSourceType.Unknown;
    public bool IsMeasure { get; set; }
    public bool IsDimension { get; set; }
    public bool IsCalculated { get; set; }
    public int Order { get; set; }
    public bool IsVisible { get; set; } = true;
    public string? FormatString { get; set; }
    public string? DefaultFormat { get; set; }
    public bool AllowSorting { get; set; } = true;
    public bool AllowFiltering { get; set; } = true;
    public bool AllowRules { get; set; } = true;
    public bool VisibleInColumnSelector { get; set; } = true;
    public bool VisibleInAdvancedMode { get; set; }
    public string CatalogCategory { get; set; } = string.Empty;
    public string CatalogCanonicalKey { get; set; } = string.Empty;

    public override string ToString() => DisplayName;
}

public sealed class ReportFieldDefinition
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string SourceTable { get; set; } = string.Empty;
    public string SourceField { get; set; } = string.Empty;
    public string DaxExpression { get; set; } = string.Empty;
    public ReportColumnSourceType SourceType { get; set; } = ReportColumnSourceType.Unknown;
    public ReportFieldDataType DataType { get; set; } = ReportFieldDataType.Unknown;
    public bool IsMeasure { get; set; }
    public bool IsDimension { get; set; }
    public bool IsCalculated { get; set; }
    public string DefaultFormat { get; set; } = string.Empty;
    public bool AllowSorting { get; set; } = true;
    public bool AllowFiltering { get; set; } = true;
    public bool AllowRules { get; set; } = true;
    public int DefaultOrder { get; set; }
    public ReportFieldUsageCategory UsageCategory { get; set; } = ReportFieldUsageCategory.Unknown;
    public string CanonicalKey { get; set; } = string.Empty;
    public bool VisibleInColumnSelector { get; set; } = true;
    public bool VisibleInRuleScopeSelector { get; set; } = true;
    public bool VisibleInRuleMetricSelector { get; set; } = true;
    public bool VisibleInAdvancedMode { get; set; }
}

public sealed class ReportSortDefinition
{
    public string FieldKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ReportSortDirection Direction { get; set; } = ReportSortDirection.Asc;
    public int Priority { get; set; }
}

public sealed class ReportFormatDefinition
{
    public string FieldKey { get; set; } = string.Empty;
    public ReportFormatType FormatType { get; set; } = ReportFormatType.Auto;
    public int DecimalPlaces { get; set; } = 2;
    public bool UseThousandsSeparator { get; set; } = true;
    public string CurrencySymbol { get; set; } = "C$";
    public ReportAlignment Alignment { get; set; } = ReportAlignment.Auto;
    public bool HideZeros { get; set; }
}

public sealed class ReportRuleCondition
{
    public string Field { get; set; } = string.Empty;
    public RuleOperatorType Operator { get; set; } = RuleOperatorType.GreaterThan;
    public RuleValueSourceType ValueSource { get; set; } = RuleValueSourceType.Constant;
    public string? ComparisonValue { get; set; }
    public string? ComparisonField { get; set; }
    public string? RangeEndValue { get; set; }
}

public sealed class ReportRuleDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nombre { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public ReportRuleType RuleType { get; set; } = ReportRuleType.Visual;
    public ReportRuleScope Scope { get; set; } = ReportRuleScope.Cell;
    public ReportLogicalOperator LogicalGroup { get; set; } = ReportLogicalOperator.And;
    public string Campo { get; set; } = string.Empty;
    public string CampoObjetivo
    {
        get => Campo;
        set => Campo = value ?? string.Empty;
    }
    public RuleOperatorType Operador { get; set; } = RuleOperatorType.GreaterThan;
    public string Valor { get; set; } = string.Empty;
    public string? ValorHasta { get; set; }
    public RuleValueSourceType ValueSource { get; set; } = RuleValueSourceType.Constant;
    public string? ComparisonField { get; set; }
    public RuleActionType Accion { get; set; } = RuleActionType.ChangeCellColor;
    public string? ValorAccion { get; set; }
    public int Priority { get; set; } = 100;
    public string Description { get; set; } = string.Empty;
    public ReportRuleOutputType OutputType { get; set; } = ReportRuleOutputType.Visual;
    public List<ReportRuleCondition> Conditions { get; set; } = new();
}

public sealed class CustomReportRuleActionDefinition
{
    public CustomReportRuleActionType Type { get; set; } = CustomReportRuleActionType.None;
    public decimal? Amount { get; set; }
    public string Currency { get; set; } = "NIO";
    public string Value { get; set; } = string.Empty;
}

public sealed class CustomReportRuleTierConditionDefinition
{
    public string Operator { get; set; } = ">=";
    public decimal Value { get; set; }
}

public sealed class CustomReportRuleTierDefinition
{
    public string Name { get; set; } = string.Empty;
    public CustomReportRuleTierConditionDefinition Condition { get; set; } = new();
    public CustomReportRuleActionDefinition Result { get; set; } = new();
}

public sealed class GuidedRuleDescriptionItemDefinition
{
    public string Value { get; set; } = string.Empty;
    public CustomReportRuleActionDefinition SuccessAction { get; set; } = new();
    public CustomReportRuleActionDefinition FailureAction { get; set; } = new();
}

public sealed class GuidedRuleRouteOverrideDefinition
{
    public List<string> Routes { get; set; } = new();
    public decimal? Target { get; set; }
    public CustomReportRuleActionDefinition SuccessAction { get; set; } = new();
    public CustomReportRuleActionDefinition FailureAction { get; set; } = new();
}

public sealed class CustomReportTemplateRuleDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Dimension { get; set; } = string.Empty;
    public string DimensionLabel { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public string MetricLabel { get; set; } = string.Empty;
    public string Operator { get; set; } = ">";
    public decimal Value { get; set; }
    public CustomReportRuleEvaluationType EvaluationType { get; set; } = CustomReportRuleEvaluationType.MarkMatches;
    public string Comparison { get; set; } = ">=";
    public decimal? Target { get; set; }
    public CustomReportRuleActionDefinition SuccessAction { get; set; } = new();
    public CustomReportRuleActionDefinition FailureAction { get; set; } = new();
    public List<CustomReportRuleTierDefinition> Tiers { get; set; } = new();
    public GuidedRuleBusinessType BusinessType { get; set; } = GuidedRuleBusinessType.None;
    public List<GuidedRuleDescriptionItemDefinition> DescriptionItems { get; set; } = new();
    public string RouteField { get; set; } = string.Empty;
    public string SellerField { get; set; } = string.Empty;
    public List<GuidedRuleRouteOverrideDefinition> RouteOverrides { get; set; } = new();
}

public sealed class AggregationRuleDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nombre { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string GroupByField { get; set; } = string.Empty;
    public string ConditionField { get; set; } = string.Empty;
    public RuleOperatorType ConditionOperator { get; set; } = RuleOperatorType.GreaterThanOrEqual;
    public string ConditionValue { get; set; } = string.Empty;
    public int MinimumMatches { get; set; }
    public RuleActionType SuccessActionType { get; set; } = RuleActionType.CalculatePremio;
    public string SuccessActionValue { get; set; } = string.Empty;
    public RuleActionType FailureActionType { get; set; } = RuleActionType.CalculateAfectacion;
    public string FailureActionValue { get; set; } = string.Empty;
    public int Priority { get; set; } = 100;
}

public sealed class ReportExecutionRequest
{
    public Guid? TemplateId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public ReportSourceMode SourceMode { get; set; } = ReportSourceMode.ExistingReport;
    public string TipoReporte { get; set; } = string.Empty;
    public string ReporteOrigenReal { get; set; } = string.Empty;
    public string ReporteOrigen
    {
        get => string.IsNullOrWhiteSpace(ReporteOrigenReal) ? TipoReporte : ReporteOrigenReal;
        set
        {
            TipoReporte = value ?? string.Empty;
            ReporteOrigenReal = value ?? string.Empty;
        }
    }
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
    public int Mes { get; set; }
    public int Anio { get; set; }
    public string? DatasetId { get; set; }
    public List<string> Vendedores { get; set; } = new();
    public List<ReportFilterDefinition> FiltrosBase { get; set; } = new();
    public List<ReportColumnDefinition> Columnas { get; set; } = new();
    public List<ReportFieldDefinition> Fields { get; set; } = new();
    public List<ReportSortDefinition> Sorting { get; set; } = new();
    public List<ReportFormatDefinition> Formatting { get; set; } = new();
    public List<ReportRuleDefinition> Reglas { get; set; } = new();
    public List<AggregationRuleDefinition> AggregationRules { get; set; } = new();
    public List<CustomReportTemplateRuleDefinition> GuidedRules { get; set; } = new();

    [JsonIgnore]
    public List<ReportRuleDefinition> Rules
    {
        get => Reglas;
        set => Reglas = value ?? new List<ReportRuleDefinition>();
    }
}

public sealed class CustomReportRuleResult
{
    public Guid RuleId { get; set; } = Guid.NewGuid();
    public string RuleName { get; set; } = string.Empty;
    public string DimensionLabel { get; set; } = string.Empty;
    public string MetricLabel { get; set; } = string.Empty;
    public string AppliedDescription { get; set; } = string.Empty;
    public string AppliedRoute { get; set; } = string.Empty;
    public string AppliedSeller { get; set; } = string.Empty;
    public string ConditionSummary { get; set; } = string.Empty;
    public string EvaluationSummary { get; set; } = string.Empty;
    public decimal MatchCount { get; set; }
    public decimal EvaluationValue { get; set; }
    public bool Succeeded { get; set; }
    public string ResultType { get; set; } = string.Empty;
    public string ResultText { get; set; } = string.Empty;
    public decimal? ResultAmount { get; set; }
    public string Currency { get; set; } = "NIO";
    public string TierName { get; set; } = string.Empty;

    public override string ToString()
    {
        var outcome = ResultAmount.HasValue
            ? $"{Currency} {ResultAmount.Value:0.##}"
            : ResultText;

        return $"{RuleName}: {(Succeeded ? "Cumple" : "No cumple")} {outcome}".Trim();
    }
}

public sealed class ReportCellStyle
{
    public int RowIndex { get; set; }
    public string ColumnKey { get; set; } = string.Empty;
    public string? BackgroundColorHex { get; set; }
    public string? TextColorHex { get; set; }
    public string? Icon { get; set; }
    public string? TextOverride { get; set; }
    public string? Scope { get; set; }
    public string? RuleName { get; set; }
}

public sealed class ReportExecutionResult
{
    public List<ReportColumnDefinition> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public List<ReportCellStyle> Styles { get; set; } = new();
    public Dictionary<string, object?> Summaries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Warnings { get; set; } = new();
    public List<CustomReportRuleResult> RuleResults { get; set; } = new();

    [JsonIgnore]
    public Dictionary<string, object?> Summary
    {
        get => Summaries;
        set => Summaries = value ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }
}
