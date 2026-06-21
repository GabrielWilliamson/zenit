using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Zenit.Models.CustomReports;
using Zenit.ViewModels;

namespace Zenit.Services;

public sealed class ReportTemplateRuleSchemaService
{
    private const string GeneralRulesKey = "rules_general";
    private const string TieredRulesKey = "rules_tiered";
    private const string LegacyRulesKey = "rules_legacy";

    public ReportTemplateRuleParseResult Parse(
        IEnumerable<JsonElement>? rawRules,
        IReadOnlyList<ReportFieldDefinition>? fieldCatalog = null)
    {
        var result = new ReportTemplateRuleParseResult();
        if (rawRules == null)
            return result;

        foreach (var rawRule in rawRules)
        {
            if (rawRule.ValueKind != JsonValueKind.Object)
                continue;

            if (TryParseGroupedRules(rawRule, fieldCatalog, result))
                continue;

            if (TryParseGuidedRule(rawRule, fieldCatalog, out var guidedRule))
            {
                result.GuidedRules.Add(guidedRule);
                continue;
            }

            if (TryConvertLegacyRule(rawRule, fieldCatalog, out guidedRule))
            {
                result.GuidedRules.Add(guidedRule);
                continue;
            }

            result.PreservedLegacyRules.Add(rawRule.Clone());
            result.LegacyRuleSummaries.Add(BuildLegacySummary(rawRule));
        }

        return result;
    }

    public List<JsonElement> BuildRulesJson(
        IEnumerable<CustomReportTemplateRuleDefinition> guidedRules,
        IEnumerable<JsonElement>? preservedLegacyRules = null)
    {
        ArgumentNullException.ThrowIfNull(guidedRules);

        var generalRules = new JsonArray();
        var tieredRules = new JsonArray();
        foreach (var rule in guidedRules)
        {
            var node = BuildGuidedRuleNode(rule);
            if (rule.Tiers.Count > 0)
                tieredRules.Add(node);
            else
                generalRules.Add(node);
        }

        var grouped = new JsonObject
        {
            [GeneralRulesKey] = generalRules,
            [TieredRulesKey] = tieredRules
        };

        if (preservedLegacyRules != null)
        {
            var legacyArray = new JsonArray();
            foreach (var legacyRule in preservedLegacyRules)
                legacyArray.Add(ToNode(legacyRule.Clone()));

            if (legacyArray.Count > 0)
                grouped[LegacyRulesKey] = legacyArray;
        }

        return new List<JsonElement> { ToElement(grouped) };
    }

    private static JsonObject BuildGuidedRuleNode(CustomReportTemplateRuleDefinition rule)
    {
        var node = new JsonObject
        {
            ["kind"] = "guided_rule",
            ["id"] = rule.Id.ToString(),
            ["name"] = rule.Name,
            ["dimension"] = rule.Dimension,
            ["dimensionLabel"] = rule.DimensionLabel,
            ["metric"] = rule.Metric,
            ["metricLabel"] = rule.MetricLabel,
            ["operator"] = rule.Operator,
            ["value"] = rule.Value,
            ["evaluationType"] = ToSchemaValue(rule.EvaluationType),
            ["comparison"] = rule.Comparison
        };

        if (rule.BusinessType != GuidedRuleBusinessType.None)
            node["businessType"] = ToSchemaValue(rule.BusinessType);

        if (rule.Target.HasValue)
            node["target"] = rule.Target.Value;

        if (rule.Tiers.Count > 0)
            node["tiers"] = BuildTiersNode(rule.Tiers);

        if (rule.DescriptionItems.Count > 0)
            node["descriptionItems"] = BuildDescriptionItemsNode(rule.DescriptionItems);

        if (!string.IsNullOrWhiteSpace(rule.RouteField))
            node["routeField"] = rule.RouteField;

        if (!string.IsNullOrWhiteSpace(rule.SellerField))
            node["sellerField"] = rule.SellerField;

        if (rule.RouteOverrides.Count > 0)
            node["routeOverrides"] = BuildRouteOverridesNode(rule.RouteOverrides);

        if (rule.SuccessAction.Type != CustomReportRuleActionType.None)
            node["successAction"] = BuildActionNode(rule.SuccessAction);

        if (rule.FailureAction.Type != CustomReportRuleActionType.None)
            node["failureAction"] = BuildActionNode(rule.FailureAction);

        return node;
    }

    private static bool TryParseGroupedRules(
        JsonElement rawRule,
        IReadOnlyList<ReportFieldDefinition>? fieldCatalog,
        ReportTemplateRuleParseResult result)
    {
        var hasGeneral = rawRule.TryGetProperty(GeneralRulesKey, out var general);
        var hasTiered = rawRule.TryGetProperty(TieredRulesKey, out var tiered);

        if (!hasGeneral && !hasTiered)
        {
            return false;
        }

        if (hasGeneral)
            ParseRulesArray(general, fieldCatalog, result);

        if (hasTiered)
            ParseRulesArray(tiered, fieldCatalog, result);

        if (rawRule.TryGetProperty(LegacyRulesKey, out var legacyNode))
            ParseLegacyArray(legacyNode, result);

        return true;
    }

    private static void ParseRulesArray(
        JsonElement source,
        IReadOnlyList<ReportFieldDefinition>? fieldCatalog,
        ReportTemplateRuleParseResult result)
    {
        if (source.ValueKind != JsonValueKind.Array)
            return;

        foreach (var ruleNode in source.EnumerateArray())
        {
            if (ruleNode.ValueKind != JsonValueKind.Object)
                continue;

            if (TryParseGuidedRule(ruleNode, fieldCatalog, out var guidedRule))
            {
                result.GuidedRules.Add(guidedRule);
                continue;
            }

            if (TryConvertLegacyRule(ruleNode, fieldCatalog, out guidedRule))
            {
                result.GuidedRules.Add(guidedRule);
                continue;
            }

            result.PreservedLegacyRules.Add(ruleNode.Clone());
            result.LegacyRuleSummaries.Add(BuildLegacySummary(ruleNode));
        }
    }

    private static void ParseLegacyArray(JsonElement source, ReportTemplateRuleParseResult result)
    {
        if (source.ValueKind != JsonValueKind.Array)
            return;

        foreach (var legacyRule in source.EnumerateArray())
        {
            if (legacyRule.ValueKind != JsonValueKind.Object)
                continue;

            result.PreservedLegacyRules.Add(legacyRule.Clone());
            result.LegacyRuleSummaries.Add(BuildLegacySummary(legacyRule));
        }
    }

    private static bool TryParseGuidedRule(
        JsonElement rawRule,
        IReadOnlyList<ReportFieldDefinition>? fieldCatalog,
        out CustomReportTemplateRuleDefinition guidedRule)
    {
        guidedRule = new CustomReportTemplateRuleDefinition();

        var evaluationText = GetString(rawRule, "evaluationType");
        if (string.IsNullOrWhiteSpace(evaluationText))
            return false;

        var dimensionRaw = GetString(rawRule, "dimension");
        var metricRaw = GetString(rawRule, "metric");
        if (string.IsNullOrWhiteSpace(metricRaw))
            return false;

        var dimensionField = ResolveField(fieldCatalog, dimensionRaw, true);
        var metricField = ResolveField(fieldCatalog, metricRaw, false);
        var dimensionLabel = string.IsNullOrWhiteSpace(GetString(rawRule, "dimensionLabel"))
            ? dimensionField?.DisplayName ?? Humanize(dimensionRaw)
            : GetString(rawRule, "dimensionLabel");
        var metricLabel = string.IsNullOrWhiteSpace(GetString(rawRule, "metricLabel"))
            ? metricField?.DisplayName ?? Humanize(metricRaw)
            : GetString(rawRule, "metricLabel");

        guidedRule = new CustomReportTemplateRuleDefinition
        {
            Id = TryParseGuid(GetString(rawRule, "id")),
            Name = GetString(rawRule, "name"),
            Dimension = dimensionField?.SourceField ?? dimensionField?.Key ?? dimensionRaw,
            DimensionLabel = dimensionLabel,
            Metric = metricField?.SourceField ?? metricField?.Key ?? metricRaw,
            MetricLabel = metricLabel,
            Operator = NormalizeOperator(GetString(rawRule, "operator")),
            Value = GetDecimal(rawRule, "value"),
            EvaluationType = ParseEvaluationType(evaluationText),
            Comparison = NormalizeOperator(GetString(rawRule, "comparison"), ">="),
            Target = TryGetDecimal(rawRule, "target", out var target) ? target : null,
            BusinessType = ParseBusinessType(GetString(rawRule, "businessType")),
            DescriptionItems = ParseDescriptionItems(rawRule),
            RouteField = GetString(rawRule, "routeField"),
            SellerField = GetString(rawRule, "sellerField"),
            RouteOverrides = ParseRouteOverrides(rawRule),
            SuccessAction = ParseAction(rawRule, "successAction"),
            FailureAction = ParseAction(rawRule, "failureAction"),
            Tiers = ParseTiers(rawRule)
        };

        if (guidedRule.SuccessAction.Type == CustomReportRuleActionType.None
            && guidedRule.Tiers.Count == 0
            && guidedRule.EvaluationType == CustomReportRuleEvaluationType.MarkMatches)
        {
            guidedRule.SuccessAction = new CustomReportRuleActionDefinition
            {
                Type = CustomReportRuleActionType.Mark
            };
        }

        return true;
    }

    private static bool TryConvertLegacyRule(
        JsonElement rawRule,
        IReadOnlyList<ReportFieldDefinition>? fieldCatalog,
        out CustomReportTemplateRuleDefinition guidedRule)
    {
        guidedRule = new CustomReportTemplateRuleDefinition();

        var type = GetString(rawRule, "type");
        if (string.IsNullOrWhiteSpace(type) || type.Contains("escala", StringComparison.OrdinalIgnoreCase))
            return false;

        if (HasSpecificRoutes(rawRule))
            return false;

        if (rawRule.TryGetProperty("overrides", out var overrides)
            && overrides.ValueKind == JsonValueKind.Array
            && overrides.GetArrayLength() > 0)
        {
            return false;
        }

        if (!rawRule.TryGetProperty("conditions", out var conditions)
            || conditions.ValueKind != JsonValueKind.Array
            || conditions.GetArrayLength() != 1)
        {
            return false;
        }

        var condition = conditions[0];
        var metricRaw = GetString(condition, "metric");
        if (string.IsNullOrWhiteSpace(metricRaw) || !TryGetDecimal(condition, "value", out var threshold))
            return false;

        var dimensionRaw = GetString(rawRule, "scope");
        if (string.IsNullOrWhiteSpace(dimensionRaw))
            dimensionRaw = "DESCRIPCION";

        var dimensionField = ResolveField(fieldCatalog, dimensionRaw, true);
        var metricField = ResolveField(fieldCatalog, metricRaw, false);

        var successAction = TryGetRewardAction(rawRule);
        if (successAction.Type == CustomReportRuleActionType.None)
            successAction = new CustomReportRuleActionDefinition { Type = CustomReportRuleActionType.Mark };

        guidedRule = new CustomReportTemplateRuleDefinition
        {
            Id = Guid.NewGuid(),
            Name = GetString(rawRule, "line"),
            Dimension = dimensionField?.SourceField ?? dimensionField?.Key ?? dimensionRaw,
            DimensionLabel = dimensionField?.DisplayName ?? Humanize(dimensionRaw),
            Metric = metricField?.SourceField ?? metricField?.Key ?? metricRaw,
            MetricLabel = metricField?.DisplayName ?? Humanize(metricRaw),
            Operator = NormalizeOperator(GetString(condition, "operator")),
            Value = threshold,
            EvaluationType = CustomReportRuleEvaluationType.MarkMatches,
            Comparison = ">=",
            SuccessAction = successAction,
            FailureAction = ParseLegacyFailureAction(rawRule)
        };

        return true;
    }

    private static LegacyRuleUiModel BuildLegacySummary(JsonElement rawRule)
    {
        var type = GetString(rawRule, "type");
        var name = GetString(rawRule, "name");
        if (string.IsNullOrWhiteSpace(name))
            name = GetString(rawRule, "line");
        if (string.IsNullOrWhiteSpace(name))
            name = type.Contains("escala", StringComparison.OrdinalIgnoreCase)
                ? "Regla escalonada heredada"
                : "Regla heredada";

        var summary = type.Contains("escala", StringComparison.OrdinalIgnoreCase)
            ? "Incluye tiers o logica escalonada heredada. Se conserva sin cambios."
            : "Incluye rutas, overrides o condiciones legacy. Se conserva sin cambios.";

        return new LegacyRuleUiModel
        {
            Name = name,
            Summary = summary
        };
    }

    private static CustomReportRuleActionDefinition TryGetRewardAction(JsonElement rawRule)
    {
        if (!rawRule.TryGetProperty("reward", out var rewardNode) || rewardNode.ValueKind != JsonValueKind.Object)
            return new CustomReportRuleActionDefinition { Type = CustomReportRuleActionType.None };

        return new CustomReportRuleActionDefinition
        {
            Type = CustomReportRuleActionType.Reward,
            Amount = TryGetDecimal(rewardNode, "amount", out var amount) ? amount : null,
            Currency = GetString(rawRule, "currency", "NIO")
        };
    }

    private static CustomReportRuleActionDefinition ParseLegacyFailureAction(JsonElement rawRule)
    {
        if (!rawRule.TryGetProperty("visual", out var visual) || visual.ValueKind != JsonValueKind.Object)
            return new CustomReportRuleActionDefinition { Type = CustomReportRuleActionType.None };

        if (!visual.TryGetProperty("on_fail", out var onFail) || onFail.ValueKind != JsonValueKind.Object)
            return new CustomReportRuleActionDefinition { Type = CustomReportRuleActionType.None };

        var status = GetString(onFail, "status");
        if (string.IsNullOrWhiteSpace(status))
            return new CustomReportRuleActionDefinition { Type = CustomReportRuleActionType.None };

        return new CustomReportRuleActionDefinition
        {
            Type = CustomReportRuleActionType.SetStatus,
            Value = status
        };
    }

    private static CustomReportRuleActionDefinition ParseAction(JsonElement rawRule, string propertyName)
    {
        if (!rawRule.TryGetProperty(propertyName, out var actionNode) || actionNode.ValueKind != JsonValueKind.Object)
            return new CustomReportRuleActionDefinition { Type = CustomReportRuleActionType.None };

        return new CustomReportRuleActionDefinition
        {
            Type = ParseActionType(GetString(actionNode, "type")),
            Amount = TryGetDecimal(actionNode, "amount", out var amount) ? amount : null,
            Currency = GetString(actionNode, "currency", "NIO"),
            Value = GetString(actionNode, "value")
        };
    }

    private static List<CustomReportRuleTierDefinition> ParseTiers(JsonElement rawRule)
    {
        var result = new List<CustomReportRuleTierDefinition>();
        if (!rawRule.TryGetProperty("tiers", out var tiersNode) || tiersNode.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var tierNode in tiersNode.EnumerateArray())
        {
            if (tierNode.ValueKind != JsonValueKind.Object)
                continue;

            var conditionOperator = ">=";
            var conditionValue = 0m;

            if (tierNode.TryGetProperty("condition", out var conditionNode) && conditionNode.ValueKind == JsonValueKind.Object)
            {
                conditionOperator = NormalizeOperator(GetString(conditionNode, "operator"), ">=");
                conditionValue = GetDecimal(conditionNode, "value");
            }
            else
            {
                conditionOperator = NormalizeOperator(GetString(tierNode, "operator"), ">=");
                conditionValue = GetDecimal(tierNode, "value");
            }

            var resultAction = tierNode.TryGetProperty("result", out var resultNode) && resultNode.ValueKind == JsonValueKind.Object
                ? ParseAction(tierNode, "result")
                : ParseAction(tierNode, "action");

            result.Add(new CustomReportRuleTierDefinition
            {
                Name = GetString(tierNode, "name"),
                Condition = new CustomReportRuleTierConditionDefinition
                {
                    Operator = conditionOperator,
                    Value = conditionValue
                },
                Result = resultAction
            });
        }

        return result;
    }

    private static List<GuidedRuleDescriptionItemDefinition> ParseDescriptionItems(JsonElement rawRule)
    {
        var result = new List<GuidedRuleDescriptionItemDefinition>();
        if (!rawRule.TryGetProperty("descriptionItems", out var itemsNode) || itemsNode.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var itemNode in itemsNode.EnumerateArray())
        {
            if (itemNode.ValueKind != JsonValueKind.Object)
                continue;

            result.Add(new GuidedRuleDescriptionItemDefinition
            {
                Value = GetString(itemNode, "value"),
                SuccessAction = ParseAction(itemNode, "successAction"),
                FailureAction = ParseAction(itemNode, "failureAction")
            });
        }

        return result;
    }

    private static List<GuidedRuleRouteOverrideDefinition> ParseRouteOverrides(JsonElement rawRule)
    {
        var result = new List<GuidedRuleRouteOverrideDefinition>();
        if (!rawRule.TryGetProperty("routeOverrides", out var overridesNode) || overridesNode.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var overrideNode in overridesNode.EnumerateArray())
        {
            if (overrideNode.ValueKind != JsonValueKind.Object)
                continue;

            result.Add(new GuidedRuleRouteOverrideDefinition
            {
                Routes = ParseStringArray(overrideNode, "routes"),
                Target = TryGetDecimal(overrideNode, "target", out var target) ? target : null,
                SuccessAction = ParseAction(overrideNode, "successAction"),
                FailureAction = ParseAction(overrideNode, "failureAction")
            });
        }

        return result;
    }

    private static List<string> ParseStringArray(JsonElement rawNode, string propertyName)
    {
        if (!rawNode.TryGetProperty(propertyName, out var arrayNode) || arrayNode.ValueKind != JsonValueKind.Array)
            return new List<string>();

        return arrayNode.EnumerateArray()
            .Select(item => item.ValueKind switch
            {
                JsonValueKind.String => item.GetString(),
                JsonValueKind.Number => item.GetRawText(),
                _ => string.Empty
            })
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!.Trim())
            .ToList();
    }

    private static JsonObject BuildActionNode(CustomReportRuleActionDefinition action)
    {
        var node = new JsonObject
        {
            ["type"] = ToSchemaValue(action.Type)
        };

        if (action.Amount.HasValue)
            node["amount"] = action.Amount.Value;

        if (!string.IsNullOrWhiteSpace(action.Currency))
            node["currency"] = action.Currency;

        if (!string.IsNullOrWhiteSpace(action.Value))
            node["value"] = action.Value;

        return node;
    }

    private static JsonArray BuildDescriptionItemsNode(IReadOnlyList<GuidedRuleDescriptionItemDefinition> items)
    {
        var array = new JsonArray();

        foreach (var item in items.Where(item => !string.IsNullOrWhiteSpace(item.Value)))
        {
            var node = new JsonObject
            {
                ["value"] = item.Value
            };

            if (item.SuccessAction.Type != CustomReportRuleActionType.None)
                node["successAction"] = BuildActionNode(item.SuccessAction);

            if (item.FailureAction.Type != CustomReportRuleActionType.None)
                node["failureAction"] = BuildActionNode(item.FailureAction);

            array.Add(node);
        }

        return array;
    }

    private static JsonArray BuildRouteOverridesNode(IReadOnlyList<GuidedRuleRouteOverrideDefinition> overrides)
    {
        var array = new JsonArray();

        foreach (var overrideDefinition in overrides)
        {
            var node = new JsonObject();

            if (overrideDefinition.Routes.Count > 0)
            {
                var routes = new JsonArray();
                foreach (var route in overrideDefinition.Routes.Where(route => !string.IsNullOrWhiteSpace(route)))
                    routes.Add(route);

                node["routes"] = routes;
            }

            if (overrideDefinition.Target.HasValue)
                node["target"] = overrideDefinition.Target.Value;

            if (overrideDefinition.SuccessAction.Type != CustomReportRuleActionType.None)
                node["successAction"] = BuildActionNode(overrideDefinition.SuccessAction);

            if (overrideDefinition.FailureAction.Type != CustomReportRuleActionType.None)
                node["failureAction"] = BuildActionNode(overrideDefinition.FailureAction);

            array.Add(node);
        }

        return array;
    }

    private static JsonArray BuildTiersNode(IEnumerable<CustomReportRuleTierDefinition> tiers)
    {
        var array = new JsonArray();
        foreach (var tier in tiers)
        {
            var tierNode = new JsonObject
            {
                ["condition"] = new JsonObject
                {
                    ["operator"] = NormalizeOperator(tier.Condition.Operator, ">="),
                    ["value"] = tier.Condition.Value
                },
                ["result"] = BuildActionNode(tier.Result)
            };

            if (!string.IsNullOrWhiteSpace(tier.Name))
                tierNode["name"] = tier.Name;

            array.Add(tierNode);
        }

        return array;
    }

    private static ReportFieldDefinition? ResolveField(
        IReadOnlyList<ReportFieldDefinition>? fieldCatalog,
        string rawValue,
        bool preferDimension)
    {
        if (fieldCatalog == null || fieldCatalog.Count == 0 || string.IsNullOrWhiteSpace(rawValue))
            return null;

        var normalized = NormalizeFieldIdentity(rawValue);
        var scopedCatalog = fieldCatalog
            .Where(field => preferDimension ? field.VisibleInRuleScopeSelector : field.VisibleInRuleMetricSelector)
            .ToList();

        if (scopedCatalog.Count == 0)
            scopedCatalog = fieldCatalog.ToList();

        return scopedCatalog.FirstOrDefault(field =>
                   string.Equals(NormalizeFieldIdentity(field.SourceField), normalized, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(NormalizeFieldIdentity(field.Key), normalized, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(NormalizeFieldIdentity(field.DisplayName), normalized, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(NormalizeFieldIdentity(field.CanonicalKey), normalized, StringComparison.OrdinalIgnoreCase))
               ?? scopedCatalog.FirstOrDefault(field =>
                   preferDimension
                       ? field.IsDimension && NormalizeFieldIdentity(field.DisplayName).Contains(normalized, StringComparison.OrdinalIgnoreCase)
                       : (field.IsMeasure || field.SourceType == ReportColumnSourceType.Calculated)
                         && NormalizeFieldIdentity(field.DisplayName).Contains(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasSpecificRoutes(JsonElement rawRule)
    {
        if (!rawRule.TryGetProperty("routes", out var routes) || routes.ValueKind != JsonValueKind.Array)
            return false;

        var values = routes.EnumerateArray()
            .Select(route => route.ValueKind == JsonValueKind.String ? route.GetString() : route.GetRawText())
            .Where(route => !string.IsNullOrWhiteSpace(route))
            .ToList();

        return values.Count > 0 && values.Any(route => !string.Equals(route, "*", StringComparison.OrdinalIgnoreCase));
    }

    private static Guid TryParseGuid(string rawValue)
        => Guid.TryParse(rawValue, out var id) ? id : Guid.NewGuid();

    private static string Humanize(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return string.Empty;

        var source = rawValue;
        var openBracket = source.IndexOf('[');
        var closeBracket = source.IndexOf(']');
        if (openBracket >= 0 && closeBracket > openBracket)
            source = source[(openBracket + 1)..closeBracket];

        var chars = source
            .Trim()
            .Replace("_", " ", StringComparison.Ordinal)
            .ToLowerInvariant()
            .ToCharArray();

        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(new string(chars));
    }

    private static string Normalize(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return string.Empty;

        var chars = rawValue
            .Trim()
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray();

        return new string(chars);
    }

    private static string NormalizeFieldIdentity(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return string.Empty;

        var source = rawValue.Trim();
        var open = source.LastIndexOf('[');
        var close = source.LastIndexOf(']');
        if (open >= 0 && close > open)
            source = source[(open + 1)..close];

        source = source.Replace("%", "PCT", StringComparison.Ordinal);

        var chars = source
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '+')
            .Select(char.ToUpperInvariant)
            .ToArray();

        return new string(chars);
    }

    private static string NormalizeOperator(string rawValue, string fallback = ">")
    {
        var value = string.IsNullOrWhiteSpace(rawValue) ? fallback : rawValue.Trim();
        return value is ">" or ">=" or "<" or "<=" or "=" or "!=" ? value : fallback;
    }

    private static string GetString(JsonElement rawRule, string propertyName, string fallback = "")
    {
        if (!rawRule.TryGetProperty(propertyName, out var property))
            return fallback;

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? fallback,
            JsonValueKind.Number => property.GetRawText(),
            _ => fallback
        };
    }

    private static decimal GetDecimal(JsonElement rawRule, string propertyName)
        => TryGetDecimal(rawRule, propertyName, out var value) ? value : 0m;

    private static bool TryGetDecimal(JsonElement rawRule, string propertyName, out decimal value)
    {
        value = 0m;
        if (!rawRule.TryGetProperty(propertyName, out var property))
            return false;

        if (property.ValueKind == JsonValueKind.Number)
            return property.TryGetDecimal(out value);

        if (property.ValueKind != JsonValueKind.String)
            return false;

        return decimal.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value)
               || decimal.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.CurrentCulture, out value);
    }

    private static CustomReportRuleEvaluationType ParseEvaluationType(string rawValue)
    {
        var normalized = Normalize(rawValue);
        return normalized switch
        {
            "COUNTMATCHES" => CustomReportRuleEvaluationType.CountMatches,
            "THRESHOLDBYCOUNT" => CustomReportRuleEvaluationType.ThresholdByCount,
            "THRESHOLDBYTOTAL" => CustomReportRuleEvaluationType.ThresholdByTotal,
            _ => CustomReportRuleEvaluationType.MarkMatches
        };
    }

    private static GuidedRuleBusinessType ParseBusinessType(string rawValue)
    {
        var normalized = Normalize(rawValue);
        return normalized switch
        {
            "PERDESCRIPTIONREWARD" => GuidedRuleBusinessType.PerDescriptionReward,
            "QUALIFIEDDESCRIPTIONCOUNT" => GuidedRuleBusinessType.QualifiedDescriptionCount,
            _ => GuidedRuleBusinessType.None
        };
    }

    private static CustomReportRuleActionType ParseActionType(string rawValue)
    {
        var normalized = Normalize(rawValue);
        return normalized switch
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

    private static string ToSchemaValue(CustomReportRuleEvaluationType evaluationType)
    {
        return evaluationType switch
        {
            CustomReportRuleEvaluationType.CountMatches => "count_matches",
            CustomReportRuleEvaluationType.ThresholdByCount => "threshold_by_count",
            CustomReportRuleEvaluationType.ThresholdByTotal => "threshold_by_total",
            _ => "mark_matches"
        };
    }

    private static string ToSchemaValue(CustomReportRuleActionType actionType)
    {
        return actionType switch
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
    }

    private static string ToSchemaValue(GuidedRuleBusinessType businessType)
    {
        return businessType switch
        {
            GuidedRuleBusinessType.PerDescriptionReward => "per_description_reward",
            GuidedRuleBusinessType.QualifiedDescriptionCount => "qualified_description_count",
            _ => "none"
        };
    }

    private static JsonElement ToElement(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }

    private static JsonNode ToNode(JsonElement element)
    {
        return JsonNode.Parse(element.GetRawText()) ?? new JsonObject();
    }
}

public sealed class ReportTemplateRuleParseResult
{
    public List<CustomReportTemplateRuleDefinition> GuidedRules { get; } = new();
    public List<JsonElement> PreservedLegacyRules { get; } = new();
    public List<LegacyRuleUiModel> LegacyRuleSummaries { get; } = new();
}
