using System.Globalization;
using System.Linq;
using Zenit.Models.CustomReports;

namespace Zenit.Services;

public sealed class GuidedReportRuleEngineService
{
    public GuidedRuleExecutionResult ApplyRules(
        List<Dictionary<string, object?>> rows,
        IReadOnlyList<ReportColumnDefinition> columns,
        IReadOnlyList<CustomReportTemplateRuleDefinition> rules)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rules);

        if (rows.Count == 0 || rules.Count == 0)
            return GuidedRuleExecutionResult.Empty;

        var styles = new List<ReportCellStyle>();
        var outcomes = new List<CustomReportRuleResult>();
        var allRowIndexes = Enumerable.Range(0, rows.Count).ToList();

        foreach (var rule in rules)
        {
            if (TryApplyBusinessRule(rows, columns, rule, styles, outcomes, allRowIndexes))
                continue;

            var metricKey = ResolveColumnKey(columns, rows, rule.Metric, rule.MetricLabel);
            if (string.IsNullOrWhiteSpace(metricKey))
            {
                outcomes.Add(new CustomReportRuleResult
                {
                    RuleId = rule.Id,
                    RuleName = ResolveRuleName(rule),
                    DimensionLabel = SafeLabel(rule.DimensionLabel, "Detalle"),
                    MetricLabel = SafeLabel(rule.MetricLabel, rule.Metric),
                    ConditionSummary = BuildConditionSummary(rule),
                    EvaluationSummary = "No se encontro la metrica configurada en el resultado del reporte.",
                    MatchCount = 0,
                    EvaluationValue = 0,
                    Succeeded = false,
                    ResultType = "missing_metric",
                    ResultText = "La metrica no esta disponible en esta ejecucion."
                });
                continue;
            }

            var dimensionKey = ResolveColumnKey(columns, rows, rule.Dimension, rule.DimensionLabel);
            var matchedRowIndexes = new List<int>();
            var matchedDimensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var matchedTotal = 0m;

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var row = rows[rowIndex];
                if (!TryGetValue(row, metricKey, out var metricValue))
                    continue;

                if (!EvaluateComparison(metricValue, NormalizeOperator(rule.Operator), rule.Value))
                    continue;

                matchedRowIndexes.Add(rowIndex);
                matchedDimensions.Add(ResolveDimensionIdentity(row, dimensionKey, rowIndex));

                if (TryParseDecimal(metricValue, out var numericMetric))
                    matchedTotal += numericMetric;
            }

            var matchCount = matchedDimensions.Count > 0 ? matchedDimensions.Count : matchedRowIndexes.Count;
            var evaluationValue = ResolveEvaluationValue(rule.EvaluationType, matchCount, matchedTotal, matchedRowIndexes.Count);
            var succeeded = false;
            CustomReportRuleTierDefinition? achievedTier = null;
            var appliedAction = new CustomReportRuleActionDefinition { Type = CustomReportRuleActionType.None };

            if (rule.Tiers.Count > 0)
            {
                foreach (var tier in rule.Tiers)
                {
                    if (EvaluateComparison(evaluationValue, NormalizeOperator(tier.Condition.Operator, ">="), tier.Condition.Value))
                        achievedTier = tier;
                }

                if (achievedTier != null)
                {
                    succeeded = true;
                    appliedAction = achievedTier.Result;
                }
                else
                {
                    appliedAction = rule.FailureAction;
                }
            }
            else
            {
                succeeded = EvaluateGeneralRuleSuccess(rule, evaluationValue, matchedRowIndexes.Count);

                appliedAction = succeeded ? rule.SuccessAction : rule.FailureAction;
            }

            ApplyAction(
                rows,
                styles,
                ResolveRuleName(rule),
                metricKey,
                appliedAction,
                matchedRowIndexes,
                allRowIndexes,
                rule.EvaluationType,
                succeeded);

            outcomes.Add(new CustomReportRuleResult
            {
                RuleId = rule.Id,
                RuleName = ResolveRuleName(rule),
                DimensionLabel = SafeLabel(rule.DimensionLabel, "Detalle"),
                MetricLabel = SafeLabel(rule.MetricLabel, metricKey),
                ConditionSummary = BuildConditionSummary(rule),
                EvaluationSummary = BuildEvaluationSummary(rule, matchCount, matchedTotal, evaluationValue, achievedTier),
                MatchCount = matchCount,
                EvaluationValue = evaluationValue,
                Succeeded = succeeded,
                ResultType = appliedAction.Type.ToString(),
                ResultText = BuildActionDisplayText(appliedAction, succeeded, achievedTier),
                ResultAmount = appliedAction.Amount,
                Currency = string.IsNullOrWhiteSpace(appliedAction.Currency) ? "NIO" : appliedAction.Currency,
                TierName = achievedTier == null ? string.Empty : ResolveTierName(achievedTier)
            });
        }

        return new GuidedRuleExecutionResult(styles, outcomes);
    }

    private static bool TryApplyBusinessRule(
        List<Dictionary<string, object?>> rows,
        IReadOnlyList<ReportColumnDefinition> columns,
        CustomReportTemplateRuleDefinition rule,
        List<ReportCellStyle> styles,
        List<CustomReportRuleResult> outcomes,
        IReadOnlyList<int> allRowIndexes)
    {
        return rule.BusinessType switch
        {
            GuidedRuleBusinessType.PerDescriptionReward when rule.DescriptionItems.Count > 0
                => ApplyPerDescriptionRewardRule(rows, columns, rule, outcomes, allRowIndexes),
            GuidedRuleBusinessType.QualifiedDescriptionCount
                => ApplyQualifiedDescriptionCountRule(rows, columns, rule, outcomes),
            _ => false
        };
    }

    private static bool ApplyPerDescriptionRewardRule(
        List<Dictionary<string, object?>> rows,
        IReadOnlyList<ReportColumnDefinition> columns,
        CustomReportTemplateRuleDefinition rule,
        List<CustomReportRuleResult> outcomes,
        IReadOnlyList<int> allRowIndexes)
    {
        var ruleName = ResolveRuleName(rule);
        var metricKey = ResolveColumnKey(columns, rows, rule.Metric, rule.MetricLabel);
        var dimensionKey = ResolveColumnKey(columns, rows, rule.Dimension, rule.DimensionLabel);

        if (string.IsNullOrWhiteSpace(metricKey) || string.IsNullOrWhiteSpace(dimensionKey))
        {
            outcomes.Add(new CustomReportRuleResult
            {
                RuleId = rule.Id,
                RuleName = ruleName,
                DimensionLabel = SafeLabel(rule.DimensionLabel, "Detalle"),
                MetricLabel = SafeLabel(rule.MetricLabel, rule.Metric),
                ConditionSummary = BuildConditionSummary(rule),
                EvaluationSummary = "No se encontraron los campos requeridos para evaluar la regla por descripcion.",
                MatchCount = 0,
                EvaluationValue = 0,
                Succeeded = false,
                ResultType = "missing_field",
                ResultText = "La regla no se pudo ejecutar por falta de columnas requeridas.",
                ResultAmount = 0m
            });

            return true;
        }

        var totalReward = 0m;
        var totalCurrency = string.Empty;
        var totalSucceeded = 0m;

        foreach (var item in rule.DescriptionItems.Where(item => !string.IsNullOrWhiteSpace(item.Value)))
        {
            var description = item.Value.Trim();
            var matchedRowIndexes = new List<int>();

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var row = rows[rowIndex];
                if (!TryGetValue(row, dimensionKey, out var dimensionValue)
                    || !string.Equals(dimensionValue?.ToString()?.Trim(), description, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryGetValue(row, metricKey, out var metricValue)
                    || !EvaluateComparison(metricValue, NormalizeOperator(rule.Operator), rule.Value))
                {
                    continue;
                }

                matchedRowIndexes.Add(rowIndex);
            }

            var succeeded = matchedRowIndexes.Count > 0;
            var action = succeeded
                ? ResolveAction(item.SuccessAction, rule.SuccessAction)
                : ResolveAction(item.FailureAction, rule.FailureAction);
            var amount = succeeded ? action.Amount ?? 0m : 0m;
            var currency = ResolveCurrency(action);

            if (succeeded)
            {
                totalReward += amount;
                totalSucceeded += 1;

                if (string.IsNullOrWhiteSpace(totalCurrency))
                    totalCurrency = currency;

                AppendRuleNameToRows(rows, ruleName, matchedRowIndexes);
            }

            outcomes.Add(new CustomReportRuleResult
            {
                RuleId = rule.Id,
                RuleName = ruleName,
                DimensionLabel = SafeLabel(rule.DimensionLabel, "Detalle"),
                MetricLabel = SafeLabel(rule.MetricLabel, metricKey),
                AppliedDescription = description,
                ConditionSummary = BuildConditionSummary(rule),
                EvaluationSummary = succeeded
                    ? $"Descripcion {description}: cumple la condicion configurada."
                    : $"Descripcion {description}: no alcanza la meta configurada.",
                MatchCount = matchedRowIndexes.Count,
                EvaluationValue = matchedRowIndexes.Count,
                Succeeded = succeeded,
                ResultType = succeeded ? action.Type.ToString() : CustomReportRuleActionType.None.ToString(),
                ResultText = succeeded
                    ? BuildActionDisplayText(action, succeeded, tier: null)
                    : "Sin premio para esta descripcion.",
                ResultAmount = amount,
                Currency = currency
            });
        }

        if (allRowIndexes.Count > 0)
        {
            rows[allRowIndexes[0]][$"{ruleName} (Total acumulado)"] = totalReward;
            rows[allRowIndexes[0]][$"{ruleName} (Moneda)"] = string.IsNullOrWhiteSpace(totalCurrency) ? "NIO" : totalCurrency;
        }

        outcomes.Add(new CustomReportRuleResult
        {
            RuleId = rule.Id,
            RuleName = $"{ruleName} (Total)",
            DimensionLabel = SafeLabel(rule.DimensionLabel, "Detalle"),
            MetricLabel = SafeLabel(rule.MetricLabel, metricKey),
            ConditionSummary = BuildConditionSummary(rule),
            EvaluationSummary = $"{totalSucceeded.ToString("0", CultureInfo.InvariantCulture)} descripcion(es) generaron premio.",
            MatchCount = totalSucceeded,
            EvaluationValue = totalReward,
            Succeeded = totalReward > 0m,
            ResultType = CustomReportRuleActionType.Reward.ToString(),
            ResultText = $"Total acumulado: {(string.IsNullOrWhiteSpace(totalCurrency) ? "NIO" : totalCurrency)} {totalReward.ToString("0.##", CultureInfo.InvariantCulture)}",
            ResultAmount = totalReward,
            Currency = string.IsNullOrWhiteSpace(totalCurrency) ? "NIO" : totalCurrency
        });

        return true;
    }

    private static bool ApplyQualifiedDescriptionCountRule(
        List<Dictionary<string, object?>> rows,
        IReadOnlyList<ReportColumnDefinition> columns,
        CustomReportTemplateRuleDefinition rule,
        List<CustomReportRuleResult> outcomes)
    {
        var ruleName = ResolveRuleName(rule);
        var metricKey = ResolveColumnKey(columns, rows, rule.Metric, rule.MetricLabel);
        var dimensionKey = ResolveColumnKey(columns, rows, rule.Dimension, rule.DimensionLabel);
        var routeField = string.IsNullOrWhiteSpace(rule.RouteField) ? "RUTA" : rule.RouteField;
        var routeKey = ResolveColumnKey(columns, rows, routeField, routeField);
        var sellerKey = string.IsNullOrWhiteSpace(rule.SellerField)
            ? string.Empty
            : ResolveColumnKey(columns, rows, rule.SellerField, rule.SellerField);

        if (string.IsNullOrWhiteSpace(metricKey)
            || string.IsNullOrWhiteSpace(dimensionKey)
            || string.IsNullOrWhiteSpace(routeKey))
        {
            outcomes.Add(new CustomReportRuleResult
            {
                RuleId = rule.Id,
                RuleName = ruleName,
                DimensionLabel = SafeLabel(rule.DimensionLabel, "Detalle"),
                MetricLabel = SafeLabel(rule.MetricLabel, rule.Metric),
                ConditionSummary = BuildConditionSummary(rule),
                EvaluationSummary = "No se encontraron los campos requeridos para evaluar la regla por ruta.",
                MatchCount = 0,
                EvaluationValue = 0,
                Succeeded = false,
                ResultType = "missing_field",
                ResultText = "La regla no se pudo ejecutar por falta de columnas requeridas.",
                ResultAmount = 0m
            });

            return true;
        }

        var routeGroups = rows
            .Select((row, index) => new
            {
                Row = row,
                Index = index,
                Route = ResolveRowText(row, routeKey, "Sin ruta"),
                Seller = string.IsNullOrWhiteSpace(sellerKey) ? string.Empty : ResolveRowText(row, sellerKey, string.Empty)
            })
            .GroupBy(item => item.Route, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var routeGroup in routeGroups)
        {
            var route = routeGroup.Key;
            var overrideDefinition = ResolveRouteOverride(rule.RouteOverrides, route);
            var effectiveTarget = overrideDefinition?.Target ?? rule.Target;
            var successAction = ResolveAction(overrideDefinition?.SuccessAction, rule.SuccessAction);
            var failureAction = ResolveAction(overrideDefinition?.FailureAction, rule.FailureAction);
            var matchedRows = new List<int>();
            var matchedDescriptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sellers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in routeGroup)
            {
                if (!string.IsNullOrWhiteSpace(item.Seller))
                    sellers.Add(item.Seller);

                if (!TryGetValue(item.Row, metricKey, out var metricValue)
                    || !EvaluateComparison(metricValue, NormalizeOperator(rule.Operator), rule.Value))
                {
                    continue;
                }

                matchedRows.Add(item.Index);
                matchedDescriptions.Add(ResolveDimensionIdentity(item.Row, dimensionKey, item.Index));
            }

            var matchCount = matchedDescriptions.Count > 0 ? matchedDescriptions.Count : matchedRows.Count;
            var evaluationValue = matchCount;
            var succeeded = effectiveTarget.HasValue
                ? EvaluateComparison(evaluationValue, NormalizeOperator(rule.Comparison, ">="), effectiveTarget.Value)
                : matchedRows.Count > 0;
            var action = succeeded ? successAction : failureAction;
            var amount = succeeded ? action.Amount ?? 0m : 0m;
            var sellerSummary = sellers.Count == 1 ? sellers.Single() : string.Empty;

            AppendRuleNameToRows(rows, ruleName, routeGroup.Select(item => item.Index));

            outcomes.Add(new CustomReportRuleResult
            {
                RuleId = rule.Id,
                RuleName = ruleName,
                DimensionLabel = SafeLabel(rule.DimensionLabel, "Detalle"),
                MetricLabel = SafeLabel(rule.MetricLabel, metricKey),
                AppliedRoute = route,
                AppliedSeller = sellerSummary,
                ConditionSummary = BuildConditionSummary(rule),
                EvaluationSummary = $"Ruta {route}: {matchCount.ToString("0", CultureInfo.InvariantCulture)} descripcion(es) cumplen. Objetivo: {NormalizeOperator(rule.Comparison, ">=")} {FormatNullable(effectiveTarget)}.",
                MatchCount = matchCount,
                EvaluationValue = evaluationValue,
                Succeeded = succeeded,
                ResultType = succeeded ? action.Type.ToString() : CustomReportRuleActionType.None.ToString(),
                ResultText = succeeded
                    ? BuildActionDisplayText(action, succeeded, tier: null)
                    : "No alcanza la meta de la ruta.",
                ResultAmount = amount,
                Currency = ResolveCurrency(action)
            });
        }

        return true;
    }

    private static decimal ResolveEvaluationValue(
        CustomReportRuleEvaluationType evaluationType,
        int matchCount,
        decimal matchedTotal,
        int matchedRows)
        => evaluationType switch
        {
            CustomReportRuleEvaluationType.ThresholdByTotal => matchedTotal,
            CustomReportRuleEvaluationType.MarkMatches => matchedRows,
            _ => matchCount
        };

    private static string BuildConditionSummary(CustomReportTemplateRuleDefinition rule)
        => $"{SafeLabel(rule.DimensionLabel, "Detalle")} -> {SafeLabel(rule.MetricLabel, "Metrica")} {NormalizeOperator(rule.Operator)} {rule.Value.ToString("0.##", CultureInfo.InvariantCulture)}";

    private static string BuildEvaluationSummary(
        CustomReportTemplateRuleDefinition rule,
        int matchCount,
        decimal matchedTotal,
        decimal evaluationValue,
        CustomReportRuleTierDefinition? achievedTier)
    {
        var baseSummary = rule.EvaluationType switch
        {
            CustomReportRuleEvaluationType.MarkMatches => $"{matchCount} elemento(s) cumplen la condicion y se marcaron visualmente.",
            CustomReportRuleEvaluationType.CountMatches => $"{matchCount} elemento(s) de {SafeLabel(rule.DimensionLabel, "detalle")} cumplen la condicion.",
            CustomReportRuleEvaluationType.ThresholdByCount => $"{matchCount} elemento(s) cumplen. Objetivo: {NormalizeOperator(rule.Comparison, ">=")} {FormatNullable(rule.Target)}.",
            CustomReportRuleEvaluationType.ThresholdByTotal => $"{matchedTotal.ToString("0.##", CultureInfo.InvariantCulture)} total acumulado. Objetivo: {NormalizeOperator(rule.Comparison, ">=")} {FormatNullable(rule.Target)}.",
            _ => $"{evaluationValue.ToString("0.##", CultureInfo.InvariantCulture)}"
        };

        if (achievedTier == null)
            return baseSummary;

        return $"{baseSummary} Nivel alcanzado: {ResolveTierName(achievedTier)}.";
    }

    private static void ApplyAction(
        List<Dictionary<string, object?>> rows,
        List<ReportCellStyle> styles,
        string ruleName,
        string metricKey,
        CustomReportRuleActionDefinition action,
        IReadOnlyList<int> matchedRowIndexes,
        IReadOnlyList<int> allRowIndexes,
        CustomReportRuleEvaluationType evaluationType,
        bool succeeded)
    {
        if (action.Type == CustomReportRuleActionType.None)
            return;

        var rowLevelAction = evaluationType == CustomReportRuleEvaluationType.MarkMatches
                             || action.Type is CustomReportRuleActionType.Mark or CustomReportRuleActionType.Flag;

        var targetIndexes = rowLevelAction && matchedRowIndexes.Count > 0
            ? matchedRowIndexes
            : allRowIndexes;

        switch (action.Type)
        {
            case CustomReportRuleActionType.Mark:
                foreach (var rowIndex in targetIndexes)
                {
                    rows[rowIndex][$"{ruleName} (Marca)"] = succeeded ? "Cumple" : "Revisar";
                    styles.Add(new ReportCellStyle
                    {
                        RowIndex = rowIndex,
                        ColumnKey = metricKey,
                        BackgroundColorHex = succeeded ? "#FEF3C7" : "#FEE2E2",
                        Scope = "Cell",
                        RuleName = ruleName
                    });
                }
                break;

            case CustomReportRuleActionType.Flag:
                foreach (var rowIndex in targetIndexes)
                    rows[rowIndex][$"{ruleName} (Bandera)"] = string.IsNullOrWhiteSpace(action.Value) ? "Marcado" : action.Value;
                break;

            case CustomReportRuleActionType.Approved:
                foreach (var rowIndex in targetIndexes)
                    rows[rowIndex][$"{ruleName} (Estado)"] = "Aprobado";
                break;

            case CustomReportRuleActionType.Rejected:
                foreach (var rowIndex in targetIndexes)
                    rows[rowIndex][$"{ruleName} (Estado)"] = "No aprobado";
                break;

            case CustomReportRuleActionType.SetStatus:
                foreach (var rowIndex in targetIndexes)
                    rows[rowIndex][$"{ruleName} (Estado)"] = string.IsNullOrWhiteSpace(action.Value) ? "Revision manual" : action.Value;
                break;

            case CustomReportRuleActionType.Reward:
            case CustomReportRuleActionType.Penalty:
                if (allRowIndexes.Count > 0)
                {
                    var resultRow = rows[allRowIndexes[0]];
                    resultRow[$"{ruleName} (Resultado Unico)"] = BuildActionDisplayText(action, succeeded, tier: null);
                    if (action.Amount.HasValue)
                        resultRow[$"{ruleName} (Monto Unico)"] = action.Amount.Value;
                    resultRow[$"{ruleName} (Moneda)"] = ResolveCurrency(action);
                }
                break;
        }

        AppendRuleNameToRows(rows, ruleName, targetIndexes);
    }

    private static bool EvaluateGeneralRuleSuccess(
        CustomReportTemplateRuleDefinition rule,
        decimal evaluationValue,
        int matchedRows)
    {
        return rule.EvaluationType switch
        {
            CustomReportRuleEvaluationType.MarkMatches => matchedRows > 0,
            CustomReportRuleEvaluationType.CountMatches => rule.Target.HasValue
                ? EvaluateComparison(evaluationValue, NormalizeOperator(rule.Comparison, ">="), rule.Target.Value)
                : matchedRows > 0,
            CustomReportRuleEvaluationType.ThresholdByCount => EvaluateComparison(
                evaluationValue,
                NormalizeOperator(rule.Comparison, ">="),
                rule.Target ?? 0m),
            CustomReportRuleEvaluationType.ThresholdByTotal => EvaluateComparison(
                evaluationValue,
                NormalizeOperator(rule.Comparison, ">="),
                rule.Target ?? 0m),
            _ => matchedRows > 0
        };
    }

    private static string BuildActionDisplayText(
        CustomReportRuleActionDefinition action,
        bool succeeded,
        CustomReportRuleTierDefinition? tier)
    {
        if (action.Type == CustomReportRuleActionType.None)
            return succeeded ? "Cumple sin accion adicional." : "No cumple.";

        var baseText = action.Type switch
        {
            CustomReportRuleActionType.Mark => "Marca visual",
            CustomReportRuleActionType.Flag => string.IsNullOrWhiteSpace(action.Value) ? "Bandera" : $"Bandera: {action.Value}",
            CustomReportRuleActionType.Approved => "Estado aprobado",
            CustomReportRuleActionType.Rejected => "Estado no aprobado",
            CustomReportRuleActionType.SetStatus => string.IsNullOrWhiteSpace(action.Value) ? "Estado definido" : $"Estado: {action.Value}",
            CustomReportRuleActionType.Reward when action.Amount.HasValue => $"Premio {ResolveCurrency(action)} {action.Amount.Value.ToString("0.##", CultureInfo.InvariantCulture)}",
            CustomReportRuleActionType.Penalty when action.Amount.HasValue => $"Multa {ResolveCurrency(action)} {action.Amount.Value.ToString("0.##", CultureInfo.InvariantCulture)}",
            CustomReportRuleActionType.Reward => "Premio",
            CustomReportRuleActionType.Penalty => "Multa",
            _ => "Accion aplicada"
        };

        if (tier == null)
            return baseText;

        return $"{ResolveTierName(tier)} -> {baseText}";
    }

    private static string ResolveTierName(CustomReportRuleTierDefinition tier)
        => string.IsNullOrWhiteSpace(tier.Name)
            ? $"Nivel {NormalizeOperator(tier.Condition.Operator, ">=")} {tier.Condition.Value.ToString("0.##", CultureInfo.InvariantCulture)}"
            : tier.Name;

    private static string ResolveRuleName(CustomReportTemplateRuleDefinition rule)
        => string.IsNullOrWhiteSpace(rule.Name)
            ? $"{SafeLabel(rule.DimensionLabel, "Detalle")} - {SafeLabel(rule.MetricLabel, "Metrica")}"
            : rule.Name;

    private static string ResolveDimensionIdentity(Dictionary<string, object?> row, string dimensionKey, int rowIndex)
    {
        if (!string.IsNullOrWhiteSpace(dimensionKey) && TryGetValue(row, dimensionKey, out var value))
        {
            var text = value?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return $"ROW-{rowIndex}";
    }

    private static string ResolveColumnKey(
        IReadOnlyList<ReportColumnDefinition> columns,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string field,
        string label)
    {
        if (columns.Count > 0)
        {
            var normalizedField = Normalize(field);
            var normalizedLabel = Normalize(label);

            var column = columns.FirstOrDefault(col =>
                             Normalize(col.SourceField) == normalizedField
                             || Normalize(col.Key) == normalizedField
                             || Normalize(col.DisplayName) == normalizedField)
                         ?? columns.FirstOrDefault(col =>
                             !string.IsNullOrWhiteSpace(label)
                             && (Normalize(col.DisplayName) == normalizedLabel
                                 || Normalize(col.SourceField) == normalizedLabel
                                 || Normalize(col.Key) == normalizedLabel));

            if (column != null)
                return string.IsNullOrWhiteSpace(column.DisplayName) ? column.SourceField : column.DisplayName;
        }

        if (rows.Count == 0)
            return string.Empty;

        var rowKeys = rows[0].Keys.ToList();
        return rowKeys.FirstOrDefault(key =>
                   Normalize(key) == Normalize(field)
                   || Normalize(key) == Normalize(label))
               ?? string.Empty;
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

    private static bool EvaluateComparison(object? rawValue, string op, decimal expected)
    {
        if (!TryParseDecimal(rawValue, out var numericValue))
            return false;

        return EvaluateComparison(numericValue, op, expected);
    }

    private static bool EvaluateComparison(decimal left, string op, decimal right)
        => op switch
        {
            ">" => left > right,
            ">=" => left >= right,
            "<" => left < right,
            "<=" => left <= right,
            "=" => left == right,
            "!=" => left != right,
            _ => false
        };

    private static bool TryParseDecimal(object? rawValue, out decimal value)
    {
        value = 0m;
        if (rawValue is null)
            return false;

        if (rawValue is decimal asDecimal)
        {
            value = asDecimal;
            return true;
        }

        if (rawValue is int or long or short or byte)
        {
            value = Convert.ToDecimal(rawValue, CultureInfo.InvariantCulture);
            return true;
        }

        if (rawValue is double or float)
        {
            value = Convert.ToDecimal(rawValue, CultureInfo.InvariantCulture);
            return true;
        }

        var text = rawValue.ToString()?.Trim().Replace("%", string.Empty, StringComparison.Ordinal) ?? string.Empty;
        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value)
               || decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value);
    }

    private static string NormalizeOperator(string? value, string fallback = ">")
    {
        var result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return result is ">" or ">=" or "<" or "<=" or "=" or "!=" ? result : fallback;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var source = value.Trim();
        var open = source.LastIndexOf('[');
        var close = source.LastIndexOf(']');
        if (open >= 0 && close > open)
            source = source.Substring(open + 1, close - open - 1);

        source = source.Replace("%", "PCT", StringComparison.Ordinal);

        return new string(source
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '+')
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static string FormatNullable(decimal? value)
        => value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : "-";

    private static string SafeLabel(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static void AppendRuleNameToRows(
        List<Dictionary<string, object?>> rows,
        string ruleName,
        IEnumerable<int> rowIndexes)
    {
        foreach (var rowIndex in rowIndexes.Distinct())
        {
            var row = rows[rowIndex];
            if (!row.TryGetValue("Reglas Guiadas Aplicadas", out var current) || string.IsNullOrWhiteSpace(current?.ToString()))
            {
                row["Reglas Guiadas Aplicadas"] = ruleName;
            }
            else if (!current.ToString()!.Contains(ruleName, StringComparison.OrdinalIgnoreCase))
            {
                row["Reglas Guiadas Aplicadas"] = $"{current}; {ruleName}";
            }
        }
    }

    private static string ResolveRowText(Dictionary<string, object?> row, string key, string fallback)
    {
        if (TryGetValue(row, key, out var rawValue))
        {
            var text = rawValue?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return fallback;
    }

    private static GuidedRuleRouteOverrideDefinition? ResolveRouteOverride(
        IReadOnlyList<GuidedRuleRouteOverrideDefinition> overrides,
        string route)
    {
        var exact = overrides.FirstOrDefault(definition => definition.Routes.Any(value => string.Equals(value?.Trim(), route, StringComparison.OrdinalIgnoreCase)));
        if (exact != null)
            return exact;

        return overrides.FirstOrDefault(definition => definition.Routes.Any(value => string.Equals(value?.Trim(), "*", StringComparison.OrdinalIgnoreCase)));
    }

    private static CustomReportRuleActionDefinition ResolveAction(
        CustomReportRuleActionDefinition? preferred,
        CustomReportRuleActionDefinition fallback)
    {
        if (preferred != null
            && (preferred.Type != CustomReportRuleActionType.None
                || preferred.Amount.HasValue
                || !string.IsNullOrWhiteSpace(preferred.Value)))
        {
            return preferred;
        }

        return fallback;
    }

    private static string ResolveCurrency(CustomReportRuleActionDefinition action)
        => string.IsNullOrWhiteSpace(action.Currency) ? "NIO" : action.Currency.Trim();
}

public sealed class GuidedRuleExecutionResult
{
    public static GuidedRuleExecutionResult Empty { get; } = new(Array.Empty<ReportCellStyle>(), Array.Empty<CustomReportRuleResult>());

    public GuidedRuleExecutionResult(
        IEnumerable<ReportCellStyle> styles,
        IEnumerable<CustomReportRuleResult> outcomes)
    {
        Styles = styles.ToList();
        Outcomes = outcomes.ToList();
    }

    public List<ReportCellStyle> Styles { get; }
    public List<CustomReportRuleResult> Outcomes { get; }
}
