using Zenit.Models.CustomReports;

namespace Zenit.Services;

public sealed class ReportRuleEngineService
{
    public IReadOnlyList<ReportCellStyle> ApplyRules(
        List<Dictionary<string, object?>> rows,
        IReadOnlyList<ReportRuleDefinition> rules)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(rules);

        if (rules.Count == 0 || rows.Count == 0)
            return Array.Empty<ReportCellStyle>();

        var styles = new List<ReportCellStyle>();
        var orderedRules = rules
            .Where(r => r.IsEnabled)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Nombre, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            foreach (var rule in orderedRules)
            {
                if (!EvaluateRule(row, rule))
                    continue;

                ApplyAction(row, rowIndex, rule, styles);
            }
        }

        return styles;
    }

    private static bool EvaluateRule(Dictionary<string, object?> row, ReportRuleDefinition rule)
    {
        if (rule.Conditions.Count == 0)
        {
            if (!TryGetValue(row, rule.Campo, out var rowValue))
                return false;

            return EvaluateCondition(
                rowValue,
                rule.Operador,
                rule.Valor,
                rule.ValorHasta,
                ResolveComparisonValue(row, rule));
        }

        var conditions = rule.Conditions
            .Where(c => !string.IsNullOrWhiteSpace(c.Field))
            .ToList();

        if (conditions.Count == 0)
            return false;

        var evaluations = new List<bool>(conditions.Count);
        foreach (var condition in conditions)
        {
            if (!TryGetValue(row, condition.Field, out var value))
            {
                evaluations.Add(false);
                continue;
            }

            var expected = condition.ValueSource == RuleValueSourceType.Field && !string.IsNullOrWhiteSpace(condition.ComparisonField)
                ? ResolveComparisonValue(row, condition.ComparisonField)
                : condition.ComparisonValue ?? string.Empty;

            evaluations.Add(EvaluateCondition(value, condition.Operator, expected, condition.RangeEndValue, expected));
        }

        return rule.LogicalGroup switch
        {
            ReportLogicalOperator.Or => evaluations.Any(v => v),
            _ => evaluations.All(v => v)
        };
    }

    private static bool TryGetValue(Dictionary<string, object?> row, string field, out object? value)
    {
        if (row.TryGetValue(field, out value))
            return true;

        var pair = row.FirstOrDefault(kv => string.Equals(kv.Key, field, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(pair.Key))
            return false;

        value = pair.Value;
        return true;
    }

    private static string ResolveComparisonValue(Dictionary<string, object?> row, ReportRuleDefinition rule)
    {
        if (rule.ValueSource == RuleValueSourceType.Field && !string.IsNullOrWhiteSpace(rule.ComparisonField))
            return ResolveComparisonValue(row, rule.ComparisonField);

        return rule.Valor;
    }

    private static string ResolveComparisonValue(Dictionary<string, object?> row, string comparisonField)
    {
        return TryGetValue(row, comparisonField, out var fieldValue)
            ? fieldValue?.ToString() ?? string.Empty
            : string.Empty;
    }

    private static bool EvaluateCondition(object? rawValue, RuleOperatorType op, string expected, string? rangeEnd, string comparisonValue)
    {
        if (op == RuleOperatorType.IsEmpty)
            return rawValue is null || string.IsNullOrWhiteSpace(rawValue.ToString());

        if (op == RuleOperatorType.IsNotEmpty)
            return rawValue is not null && !string.IsNullOrWhiteSpace(rawValue.ToString());

        var leftText = rawValue?.ToString()?.Trim() ?? string.Empty;
        var rightText = expected?.Trim() ?? string.Empty;
        var rangeText = rangeEnd?.Trim() ?? string.Empty;

        if (TryParseDecimal(leftText, out var leftNumber) && TryParseDecimal(rightText, out var rightNumber))
        {
            return op switch
            {
                RuleOperatorType.GreaterThan => leftNumber > rightNumber,
                RuleOperatorType.LessThan => leftNumber < rightNumber,
                RuleOperatorType.GreaterThanOrEqual => leftNumber >= rightNumber,
                RuleOperatorType.LessThanOrEqual => leftNumber <= rightNumber,
                RuleOperatorType.Equal => leftNumber == rightNumber,
                RuleOperatorType.NotEqual => leftNumber != rightNumber,
                RuleOperatorType.Between when TryParseDecimal(rangeText, out var toNumber) => leftNumber >= rightNumber && leftNumber <= toNumber,
                _ => false
            };
        }

        if (DateTime.TryParse(leftText, out var leftDate) && DateTime.TryParse(rightText, out var rightDate))
        {
            return op switch
            {
                RuleOperatorType.GreaterThan => leftDate > rightDate,
                RuleOperatorType.LessThan => leftDate < rightDate,
                RuleOperatorType.GreaterThanOrEqual => leftDate >= rightDate,
                RuleOperatorType.LessThanOrEqual => leftDate <= rightDate,
                RuleOperatorType.Equal => leftDate == rightDate,
                RuleOperatorType.NotEqual => leftDate != rightDate,
                RuleOperatorType.Between when DateTime.TryParse(rangeText, out var toDate) => leftDate >= rightDate && leftDate <= toDate,
                _ => false
            };
        }

        var compare = string.Compare(leftText, rightText, StringComparison.OrdinalIgnoreCase);
        return op switch
        {
            RuleOperatorType.GreaterThan => compare > 0,
            RuleOperatorType.LessThan => compare < 0,
            RuleOperatorType.GreaterThanOrEqual => compare >= 0,
            RuleOperatorType.LessThanOrEqual => compare <= 0,
            RuleOperatorType.Equal => compare == 0,
            RuleOperatorType.NotEqual => compare != 0,
            RuleOperatorType.Contains => leftText.Contains(comparisonValue, StringComparison.OrdinalIgnoreCase),
            RuleOperatorType.NotContains => !leftText.Contains(comparisonValue, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool TryParseDecimal(string input, out decimal value)
    {
        input = (input ?? string.Empty).Trim().Replace("%", string.Empty);

        return decimal.TryParse(input, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value)
               || decimal.TryParse(input, out value);
    }

    private static void ApplyAction(
        Dictionary<string, object?> row,
        int rowIndex,
        ReportRuleDefinition rule,
        List<ReportCellStyle> styles)
    {
        var ruleName = string.IsNullOrWhiteSpace(rule.Nombre) ? "Regla" : rule.Nombre;
        var targetField = ResolveTargetField(rule);
        var scope = rule.Scope.ToString();

        switch (rule.Accion)
        {
            case RuleActionType.ChangeCellColor:
                styles.Add(new ReportCellStyle
                {
                    RowIndex = rowIndex,
                    ColumnKey = targetField,
                    BackgroundColorHex = string.IsNullOrWhiteSpace(rule.ValorAccion) ? "#DCFCE7" : rule.ValorAccion,
                    Scope = scope,
                    RuleName = ruleName
                });
                break;

            case RuleActionType.ChangeTextColor:
                styles.Add(new ReportCellStyle
                {
                    RowIndex = rowIndex,
                    ColumnKey = targetField,
                    TextColorHex = string.IsNullOrWhiteSpace(rule.ValorAccion) ? "#111827" : rule.ValorAccion,
                    Scope = scope,
                    RuleName = ruleName
                });
                break;

            case RuleActionType.ShowIcon:
                row[$"{targetField} (Icono)"] = string.IsNullOrWhiteSpace(rule.ValorAccion) ? "*" : rule.ValorAccion;
                break;

            case RuleActionType.ShowCheck:
                row[$"{targetField} (Check)"] = "CHECK";
                break;

            case RuleActionType.ShowText:
                row[$"{targetField} (Texto)"] = string.IsNullOrWhiteSpace(rule.ValorAccion) ? ruleName : rule.ValorAccion;
                break;

            case RuleActionType.CalculatePremio:
                row["Premio Calculado"] = ParseNumericOrText(rule.ValorAccion, "0");
                break;

            case RuleActionType.CalculateAfectacion:
                row["Afectacion Calculada"] = ParseNumericOrText(rule.ValorAccion, "0");
                break;

            case RuleActionType.SetValue:
                row[targetField] = ParseNumericOrText(rule.ValorAccion, string.Empty);
                break;

            case RuleActionType.SetSemaforo:
                row["Semaforo"] = string.IsNullOrWhiteSpace(rule.ValorAccion) ? "VERDE" : rule.ValorAccion;
                break;

            case RuleActionType.SetEstado:
                row["Estado"] = string.IsNullOrWhiteSpace(rule.ValorAccion) ? "Cumplido" : rule.ValorAccion;
                break;

            case RuleActionType.SetObservacion:
                row["Observacion"] = string.IsNullOrWhiteSpace(rule.ValorAccion) ? ruleName : rule.ValorAccion;
                break;

            default:
                break;
        }

        if (!row.TryGetValue("Reglas Aplicadas", out var existing) || string.IsNullOrWhiteSpace(existing?.ToString()))
        {
            row["Reglas Aplicadas"] = ruleName;
        }
        else
        {
            row["Reglas Aplicadas"] = $"{existing}; {ruleName}";
        }
    }

    private static string ResolveTargetField(ReportRuleDefinition rule)
    {
        if (rule.Scope == ReportRuleScope.Row)
            return "*ROW*";

        if (!string.IsNullOrWhiteSpace(rule.CampoObjetivo))
            return rule.CampoObjetivo;

        return string.IsNullOrWhiteSpace(rule.Campo) ? "Resultado" : rule.Campo;
    }

    private static object ParseNumericOrText(string? value, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (decimal.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var asDecimal))
            return asDecimal;

        if (decimal.TryParse(text, out asDecimal))
            return asDecimal;

        return text;
    }
}

