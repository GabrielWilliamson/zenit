using Zenit.Models.CustomReports;

namespace Zenit.Services;

public sealed class ReportAggregationRuleService
{
    public IReadOnlyList<ReportCellStyle> ApplyAggregationRules(
        List<Dictionary<string, object?>> rows,
        IReadOnlyList<AggregationRuleDefinition> rules)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(rules);

        if (rows.Count == 0 || rules.Count == 0)
            return Array.Empty<ReportCellStyle>();

        var styles = new List<ReportCellStyle>();
        var indexedRows = rows.Select((row, index) => new IndexedRow(index, row)).ToList();

        foreach (var rule in rules
                     .Where(r => r.IsEnabled)
                     .OrderBy(r => r.Priority)
                     .ThenBy(r => r.Nombre, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(rule.GroupByField) || string.IsNullOrWhiteSpace(rule.ConditionField))
                continue;

            var groups = indexedRows
                .GroupBy(r => ResolveText(r.Row, rule.GroupByField), StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                var matches = group.Count(item =>
                {
                    if (!TryGetValue(item.Row, rule.ConditionField, out var value))
                        return false;

                    return EvaluateCondition(value, rule.ConditionOperator, rule.ConditionValue);
                });

                var success = matches >= Math.Max(0, rule.MinimumMatches);
                var actionType = success ? rule.SuccessActionType : rule.FailureActionType;
                var actionValue = success ? rule.SuccessActionValue : rule.FailureActionValue;
                var ruleName = string.IsNullOrWhiteSpace(rule.Nombre) ? "Regla agregada" : rule.Nombre;

                foreach (var item in group)
                {
                    var row = item.Row;
                    row[$"{ruleName} (Cumplidas)"] = matches;
                    row[$"{ruleName} (Minimo)"] = Math.Max(0, rule.MinimumMatches);
                    row[$"{ruleName} (Resultado)"] = success ? "Cumple" : "No cumple";

                    ApplyAction(
                        row,
                        item.Index,
                        ruleName,
                        rule.GroupByField,
                        actionType,
                        actionValue,
                        styles);
                }
            }
        }

        return styles;
    }

    private static void ApplyAction(
        Dictionary<string, object?> row,
        int rowIndex,
        string ruleName,
        string targetField,
        RuleActionType actionType,
        string? actionValue,
        List<ReportCellStyle> styles)
    {
        switch (actionType)
        {
            case RuleActionType.ChangeCellColor:
                styles.Add(new ReportCellStyle
                {
                    RowIndex = rowIndex,
                    ColumnKey = string.IsNullOrWhiteSpace(targetField) ? "*ROW*" : targetField,
                    BackgroundColorHex = string.IsNullOrWhiteSpace(actionValue) ? "#DCFCE7" : actionValue,
                    Scope = "Row",
                    RuleName = ruleName
                });
                break;

            case RuleActionType.ChangeTextColor:
                styles.Add(new ReportCellStyle
                {
                    RowIndex = rowIndex,
                    ColumnKey = string.IsNullOrWhiteSpace(targetField) ? "*ROW*" : targetField,
                    TextColorHex = string.IsNullOrWhiteSpace(actionValue) ? "#111827" : actionValue,
                    Scope = "Row",
                    RuleName = ruleName
                });
                break;

            case RuleActionType.CalculatePremio:
                row["Premio Calculado"] = ParseNumericOrText(actionValue, "0");
                break;

            case RuleActionType.CalculateAfectacion:
                row["Afectacion Calculada"] = ParseNumericOrText(actionValue, "0");
                break;

            case RuleActionType.SetEstado:
                row["Estado"] = string.IsNullOrWhiteSpace(actionValue) ? "Cumplido" : actionValue;
                break;

            case RuleActionType.SetObservacion:
                row["Observacion"] = string.IsNullOrWhiteSpace(actionValue) ? ruleName : actionValue;
                break;

            case RuleActionType.SetSemaforo:
                row["Semaforo"] = string.IsNullOrWhiteSpace(actionValue) ? "VERDE" : actionValue;
                break;

            case RuleActionType.ShowIcon:
                row["Icono"] = string.IsNullOrWhiteSpace(actionValue) ? "*" : actionValue;
                break;

            case RuleActionType.ShowCheck:
                row["Check"] = "CHECK";
                break;

            case RuleActionType.ShowText:
                row["Texto Calculado"] = string.IsNullOrWhiteSpace(actionValue) ? ruleName : actionValue;
                break;

            case RuleActionType.SetValue:
                row[targetField] = ParseNumericOrText(actionValue, string.Empty);
                break;

            default:
                break;
        }

        if (!row.TryGetValue("Reglas Agregadas Aplicadas", out var current) || string.IsNullOrWhiteSpace(current?.ToString()))
        {
            row["Reglas Agregadas Aplicadas"] = ruleName;
        }
        else
        {
            row["Reglas Agregadas Aplicadas"] = $"{current}; {ruleName}";
        }
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

    private static bool TryGetValue(Dictionary<string, object?> row, string field, out object? value)
    {
        if (row.TryGetValue(field, out value))
            return true;

        var pair = row.FirstOrDefault(kv => string.Equals(kv.Key, field, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(pair.Key))
        {
            value = null;
            return false;
        }

        value = pair.Value;
        return true;
    }

    private static bool EvaluateCondition(object? rawValue, RuleOperatorType op, string expected)
    {
        if (op == RuleOperatorType.IsEmpty)
            return rawValue is null || string.IsNullOrWhiteSpace(rawValue.ToString());

        if (op == RuleOperatorType.IsNotEmpty)
            return rawValue is not null && !string.IsNullOrWhiteSpace(rawValue.ToString());

        var leftText = rawValue?.ToString()?.Trim() ?? string.Empty;
        var rightText = expected?.Trim() ?? string.Empty;

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
                _ => false
            };
        }

        var compare = string.Compare(leftText, rightText, StringComparison.OrdinalIgnoreCase);
        return op switch
        {
            RuleOperatorType.Equal => compare == 0,
            RuleOperatorType.NotEqual => compare != 0,
            RuleOperatorType.Contains => leftText.Contains(rightText, StringComparison.OrdinalIgnoreCase),
            RuleOperatorType.NotContains => !leftText.Contains(rightText, StringComparison.OrdinalIgnoreCase),
            RuleOperatorType.GreaterThan => compare > 0,
            RuleOperatorType.LessThan => compare < 0,
            RuleOperatorType.GreaterThanOrEqual => compare >= 0,
            RuleOperatorType.LessThanOrEqual => compare <= 0,
            _ => false
        };
    }

    private static bool TryParseDecimal(string input, out decimal value)
    {
        input = (input ?? string.Empty).Trim().Replace("%", string.Empty, StringComparison.Ordinal);
        return decimal.TryParse(input, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value)
               || decimal.TryParse(input, out value);
    }

    private static string ResolveText(Dictionary<string, object?> row, string field)
    {
        if (TryGetValue(row, field, out var value))
            return value?.ToString() ?? string.Empty;

        return string.Empty;
    }

    private sealed record IndexedRow(int Index, Dictionary<string, object?> Row);
}
