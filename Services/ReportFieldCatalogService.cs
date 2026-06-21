using System.Globalization;
using System.Text;
using Zenit.Models.CustomReports;
using Zenit.Infrastructure.Configuration;
using Zenit.Infrastructure.Logging;

namespace Zenit.Services;

public sealed class ReportFieldCatalogService
{
    private static readonly HashSet<string> CoreSourceTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "REPORTES",
        "VENDEDORES",
        "TIEMPO"
    };

    private static readonly HashSet<string> DefaultSalesSourceTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "MEDICIONES",
        "MEDICIONES_METAS",
        "MEDICIONES_METAS_CLIENTES",
        "VENTAS",
        "PREVENTA"
    };

    private static readonly HashSet<string> PreferredDimensionTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "MEDICIONES",
        "VENDEDORES",
        "CLIENTES",
        "REPORTES",
        "TIEMPO",
        "ORIGEN"
    };

    private static readonly HashSet<string> PreferredMetricTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "VENTAS",
        "MEDICIONES_METAS",
        "MEDICIONES_METAS_CLIENTES",
        "PREVENTA",
        "PRODUCTOS",
        "TIEMPO"
    };

    private static readonly HashSet<string> FactLikeTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "VENTAS",
        "PREVENTA",
        "MEDICIONES_METAS",
        "MEDICIONES_METAS_CLIENTES"
    };

    private static readonly Dictionary<string, string[]> SourceProfiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FOCOS"] = new[] { "MEDICIONES", "MEDICIONES_METAS", "MEDICIONES_METAS_CLIENTES", "VENTAS", "PREVENTA", "VENDEDORES", "TIEMPO", "REPORTES" },
        ["TAKIMBERLY"] = new[] { "MEDICIONES", "MEDICIONES_METAS", "MEDICIONES_METAS_CLIENTES", "VENTAS", "PREVENTA", "VENDEDORES", "TIEMPO", "REPORTES" },
        ["BICCATEGORIAS"] = new[] { "MEDICIONES", "MEDICIONES_METAS", "VENTAS", "PREVENTA", "VENDEDORES", "TIEMPO", "REPORTES" },
        ["SOLCATEGORIAS"] = new[] { "MEDICIONES", "MEDICIONES_METAS", "VENTAS", "PREVENTA", "VENDEDORES", "TIEMPO", "REPORTES" },
        ["SEGBAYER"] = new[] { "MEDICIONES", "MEDICIONES_METAS", "VENTAS", "PREVENTA", "VENDEDORES", "TIEMPO", "REPORTES" },
        ["PLANINCENTIVO"] = new[] { "MEDICIONES", "MEDICIONES_METAS", "MEDICIONES_METAS_CLIENTES", "VENTAS", "PREVENTA", "VENDEDORES", "TIEMPO", "REPORTES" }
    };

    private static readonly HashSet<string> DimensionNameHints = new(StringComparer.OrdinalIgnoreCase)
    {
        "COD",
        "RUTA",
        "GRUPO",
        "SUBGRUPO",
        "SUB_GRUPO",
        "DESCRIPCION",
        "NOMBRE",
        "CLIENTE",
        "REPORTE",
        "FECHA",
        "MES",
        "CANAL",
        "CIUDAD",
        "MUNICIPIO",
        "DEPARTAMENTO",
        "ORIGEN",
        "MOTIVO",
        "PRODUCTO",
        "PROVEEDOR",
        "CATEGORIA",
        "FAMILIA",
        "MARCA"
    };

    private static readonly HashSet<string> MetricNameHints = new(StringComparer.OrdinalIgnoreCase)
    {
        "COB",
        "CORD",
        "CAJ",
        "UNID",
        "LIT",
        "TON",
        "MD_",
        "PCT",
        "PREV",
        "VEN_+_PREV",
        "VENTA_+_PREV",
        "GAP",
        "DIA",
        "EXISTENCIA",
        "KC"
    };

    private static readonly Dictionary<string, string> FamilyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CAJA"] = "CAJAS",
        ["CAJAS_VEND"] = "CAJAS",
        ["CORDOBAS_VEND"] = "CORDOBAS",
        ["UNIDADES_VEND"] = "UNIDADES",
        ["LITROS_VEND"] = "LITROS",
        ["META_COBERTURA"] = "MD_COB",
        ["META_CORDOBAS"] = "MD_COR",
        ["META_CAJAS"] = "MD_CAJAS",
        ["META_UNIDADES"] = "MD_UNID"
    };

    private readonly IConfiguration _configuration;
    private readonly BiModelStructureService _biModelStructureService;
    private readonly ReportDefinitionService _definitionService;
    private readonly ReportMetadataService _metadataService;
    private readonly ILogger<ReportFieldCatalogService> _logger;

    public ReportFieldCatalogService(
        IConfiguration configuration,
        BiModelStructureService biModelStructureService,
        ReportDefinitionService definitionService,
        ReportMetadataService metadataService,
        ILogger<ReportFieldCatalogService> logger)
    {
        _configuration = configuration;
        _biModelStructureService = biModelStructureService;
        _definitionService = definitionService;
        _metadataService = metadataService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ReportFieldDefinition>> GetFieldCatalogAsync(
        string? datasetId,
        ReportSourceMode sourceMode,
        string? reporteOrigen,
        int anio,
        int mes,
        IReadOnlyList<string>? vendedores = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedDatasetId = await _metadataService
            .ResolveDatasetIdAsync(datasetId, cancellationToken)
            .ConfigureAwait(false);

        var inferredFields = new List<ReportFieldDefinition>();
        if (!string.IsNullOrWhiteSpace(reporteOrigen))
        {
            try
            {
                var inferredColumns = await _definitionService
                    .GetColumnDefinitionsAsync(
                        resolvedDatasetId,
                        reporteOrigen,
                        anio,
                        mes,
                        vendedores,
                        cancellationToken)
                    .ConfigureAwait(false);

                inferredFields.AddRange(inferredColumns.Select(FromColumn));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo inferir metadata de columnas para catalogo dinamico.");
            }
        }

        var baseFields = LoadConfiguredFields();
        var merged = new Dictionary<string, ReportFieldDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in baseFields)
        {
            var identity = BuildFieldIdentity(field);
            if (!merged.ContainsKey(identity))
                merged[identity] = CloneField(field);
        }

        foreach (var inferred in inferredFields)
        {
            var identity = BuildFieldIdentity(inferred);
            if (!merged.ContainsKey(identity))
                merged[identity] = CloneField(inferred);
        }

        if (sourceMode != ReportSourceMode.ExistingReport)
        {
            foreach (var calculated in BuildDefaultCalculatedFields())
            {
                var identity = BuildFieldIdentity(calculated);
                if (!merged.ContainsKey(identity))
                    merged[identity] = CloneField(calculated);
            }
        }

        var catalog = BuildFunctionalCatalog(
            merged.Values,
            inferredFields,
            sourceMode,
            reporteOrigen);

        return catalog
            .OrderBy(field => field.VisibleInColumnSelector ? 0 : 1)
            .ThenBy(field => GetCategoryOrder(field.UsageCategory))
            .ThenBy(field => GetSourceOrder(field.SourceType))
            .ThenBy(field => field.DefaultOrder)
            .ThenBy(field => field.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<ReportFieldDefinition> BuildFunctionalCatalog(
        IEnumerable<ReportFieldDefinition> fields,
        IReadOnlyList<ReportFieldDefinition> inferredFields,
        ReportSourceMode sourceMode,
        string? reporteOrigen)
    {
        var catalog = fields
            .Select(CloneField)
            .ToList();

        foreach (var field in catalog)
        {
            NormalizeFieldShape(field);
            var category = ClassifyField(field);
            var canonical = BuildSemanticFamily(field);

            field.CanonicalKey = canonical;
            field.UsageCategory = category;
            ApplyCategoryDefaults(field, category);
        }

        var inferredFamilies = inferredFields
            .Select(BuildSemanticFamily)
            .Where(family => !string.IsNullOrWhiteSpace(family))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allowedTables = ResolveAllowedTables(
            sourceMode,
            reporteOrigen,
            catalog,
            inferredFamilies);

        if (sourceMode == ReportSourceMode.ExistingReport)
        {
            foreach (var field in catalog)
            {
                if (!ShouldAllowBySource(field, allowedTables, inferredFamilies))
                    MoveToHidden(field);
            }
        }

        HideRawNumericVariants(catalog);
        ConsolidateDuplicates(catalog);
        return catalog;
    }

    private static void NormalizeFieldShape(ReportFieldDefinition field)
    {
        if (string.IsNullOrWhiteSpace(field.SourceField))
            field.SourceField = field.Key;

        if (string.IsNullOrWhiteSpace(field.DisplayName))
            field.DisplayName = SimplifyName(field.SourceField);

        if (string.IsNullOrWhiteSpace(field.SourceTable))
            field.SourceTable = InferSourceTable(field.SourceField);

        if (field.SourceType == ReportColumnSourceType.Unknown)
        {
            if (field.IsMeasure)
                field.SourceType = ReportColumnSourceType.Measure;
            else if (field.IsDimension)
                field.SourceType = ReportColumnSourceType.Dimension;
        }
    }

    private static ReportFieldUsageCategory ClassifyField(ReportFieldDefinition field)
    {
        if (IsArtificialUiField(field))
            return ReportFieldUsageCategory.Hidden;

        if (field.IsMeasure || field.SourceType == ReportColumnSourceType.Measure)
            return ReportFieldUsageCategory.Metric;

        if (field.IsDimension || field.SourceType == ReportColumnSourceType.Dimension)
            return ReportFieldUsageCategory.Dimension;

        if ((field.IsCalculated || field.SourceType == ReportColumnSourceType.Calculated)
            && field.DataType is ReportFieldDataType.Decimal or ReportFieldDataType.Integer)
        {
            return ReportFieldUsageCategory.Metric;
        }

        var normalized = BuildSemanticFamily(field);
        if (HasMetricHint(normalized))
            return ReportFieldUsageCategory.Metric;

        if (HasDimensionHint(normalized))
            return ReportFieldUsageCategory.Dimension;

        return ReportFieldUsageCategory.Hidden;
    }

    private static void ApplyCategoryDefaults(ReportFieldDefinition field, ReportFieldUsageCategory category)
    {
        switch (category)
        {
            case ReportFieldUsageCategory.Dimension:
                field.IsDimension = true;
                field.IsMeasure = false;
                field.AllowFiltering = true;
                field.AllowRules = true;
                field.VisibleInColumnSelector = true;
                field.VisibleInRuleScopeSelector = true;
                field.VisibleInRuleMetricSelector = false;
                field.VisibleInAdvancedMode = false;
                break;

            case ReportFieldUsageCategory.Metric:
                field.IsMeasure = true;
                field.IsDimension = false;
                field.AllowFiltering = false;
                field.AllowRules = true;
                field.VisibleInColumnSelector = true;
                field.VisibleInRuleScopeSelector = false;
                field.VisibleInRuleMetricSelector = true;
                field.VisibleInAdvancedMode = false;
                break;

            default:
                MoveToHidden(field);
                break;
        }
    }

    private static void MoveToHidden(ReportFieldDefinition field)
    {
        field.UsageCategory = ReportFieldUsageCategory.Hidden;
        field.VisibleInColumnSelector = false;
        field.VisibleInRuleScopeSelector = false;
        field.VisibleInRuleMetricSelector = false;
        field.VisibleInAdvancedMode = true;
        field.AllowRules = false;
    }

    private static HashSet<string> ResolveAllowedTables(
        ReportSourceMode sourceMode,
        string? reporteOrigen,
        IReadOnlyList<ReportFieldDefinition> catalog,
        HashSet<string> inferredFamilies)
    {
        var allowed = new HashSet<string>(CoreSourceTables, StringComparer.OrdinalIgnoreCase);
        if (sourceMode != ReportSourceMode.ExistingReport)
        {
            foreach (var table in catalog.Select(field => field.SourceTable).Where(table => !string.IsNullOrWhiteSpace(table)))
                allowed.Add(table);

            return allowed;
        }

        var sourceKey = NormalizeSourceName(reporteOrigen);
        var matchedProfile = false;
        foreach (var profile in SourceProfiles)
        {
            if (!sourceKey.Contains(profile.Key, StringComparison.OrdinalIgnoreCase))
                continue;

            matchedProfile = true;
            allowed.UnionWith(profile.Value);
        }

        if (!matchedProfile)
            allowed.UnionWith(DefaultSalesSourceTables);

        if (sourceKey.Contains("CLIENT", StringComparison.OrdinalIgnoreCase))
            allowed.Add("CLIENTES");

        if (sourceKey.Contains("PROD", StringComparison.OrdinalIgnoreCase)
            || sourceKey.Contains("INVENT", StringComparison.OrdinalIgnoreCase)
            || sourceKey.Contains("EXISTEN", StringComparison.OrdinalIgnoreCase))
        {
            allowed.Add("PRODUCTOS");
        }

        if (sourceKey.Contains("ANUL", StringComparison.OrdinalIgnoreCase)
            || sourceKey.Contains("DEVOL", StringComparison.OrdinalIgnoreCase))
        {
            allowed.Add("ANULADASXVENDEDOR");
        }

        if (sourceKey.Contains("ORIGEN", StringComparison.OrdinalIgnoreCase))
            allowed.Add("ORIGEN");

        if (inferredFamilies.Count > 0)
        {
            foreach (var field in catalog)
            {
                if (string.IsNullOrWhiteSpace(field.SourceTable))
                    continue;

                if (inferredFamilies.Contains(BuildSemanticFamily(field)))
                    allowed.Add(field.SourceTable);
            }
        }

        return allowed;
    }

    private static bool ShouldAllowBySource(
        ReportFieldDefinition field,
        IReadOnlySet<string> allowedTables,
        IReadOnlySet<string> inferredFamilies)
    {
        if (field.UsageCategory == ReportFieldUsageCategory.Hidden)
            return false;

        if (string.IsNullOrWhiteSpace(field.SourceTable))
        {
            if (inferredFamilies.Count == 0)
                return true;

            return inferredFamilies.Contains(field.CanonicalKey)
                   || IsCoreFieldFamily(field.CanonicalKey);
        }

        if (allowedTables.Contains(field.SourceTable))
            return true;

        return inferredFamilies.Contains(field.CanonicalKey);
    }

    private static void HideRawNumericVariants(List<ReportFieldDefinition> catalog)
    {
        var metricFamilies = catalog
            .Where(field => field.UsageCategory == ReportFieldUsageCategory.Metric)
            .Select(field => field.CanonicalKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var field in catalog)
        {
            if (field.UsageCategory != ReportFieldUsageCategory.Dimension)
                continue;

            if (!metricFamilies.Contains(field.CanonicalKey))
                continue;

            if (!IsLikelyRawNumericDimension(field))
                continue;

            MoveToHidden(field);
        }
    }

    private static void ConsolidateDuplicates(List<ReportFieldDefinition> catalog)
    {
        var groups = catalog
            .Where(field => field.UsageCategory != ReportFieldUsageCategory.Hidden)
            .GroupBy(field => field.CanonicalKey, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1);

        foreach (var group in groups)
        {
            var selected = group
                .OrderByDescending(GetPriorityScore)
                .ThenBy(field => field.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .First();

            foreach (var candidate in group)
            {
                if (ReferenceEquals(candidate, selected))
                    continue;

                if (candidate.UsageCategory != selected.UsageCategory
                    && !IsLikelyRawNumericDimension(candidate))
                {
                    continue;
                }

                MoveToHidden(candidate);
            }
        }
    }

    private static int GetPriorityScore(ReportFieldDefinition field)
    {
        var score = 0;
        if (field.UsageCategory == ReportFieldUsageCategory.Metric)
            score += 100;

        if (field.SourceType == ReportColumnSourceType.Measure || field.IsMeasure)
            score += 80;

        if (field.UsageCategory == ReportFieldUsageCategory.Dimension)
            score += 60;

        if (PreferredDimensionTables.Contains(field.SourceTable) && field.UsageCategory == ReportFieldUsageCategory.Dimension)
            score += 30;

        if (PreferredMetricTables.Contains(field.SourceTable) && field.UsageCategory == ReportFieldUsageCategory.Metric)
            score += 30;

        var canonical = field.CanonicalKey;
        if (canonical.EndsWith("_VEND", StringComparison.OrdinalIgnoreCase))
            score -= 40;

        if (IsLikelyRawNumericDimension(field))
            score -= 30;

        if (field.SourceType == ReportColumnSourceType.Calculated)
            score += 10;

        return score;
    }

    private static bool IsLikelyRawNumericDimension(ReportFieldDefinition field)
    {
        if (field.UsageCategory != ReportFieldUsageCategory.Dimension)
            return false;

        var canonical = field.CanonicalKey;
        var isNumeric = field.DataType is ReportFieldDataType.Integer or ReportFieldDataType.Decimal
                        || HasMetricHint(canonical);

        if (!isNumeric)
            return false;

        if (HasDimensionHint(canonical))
            return false;

        if (FactLikeTables.Contains(field.SourceTable))
            return true;

        return canonical.Contains("_VEND", StringComparison.OrdinalIgnoreCase)
               || canonical.StartsWith("META_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCoreFieldFamily(string canonicalKey)
    {
        if (string.IsNullOrWhiteSpace(canonicalKey))
            return false;

        return canonicalKey is "COD_VEND"
            or "COD_RUTA"
            or "GRUPO"
            or "SUBGRUPO"
            or "SUB_GRUPO2"
            or "DESCRIPCION"
            or "CODIGO"
            or "REPORTE"
            or "FECHA"
            or "COBERTURA"
            or "CORDOBAS"
            or "CAJAS"
            or "UNIDADES"
            or "MD_COB"
            or "MD_COR"
            or "MD_CAJAS"
            or "MD_UNID"
            or "PCT_MD_COB"
            or "PCT_MD_COR"
            or "PCT_MD_CAJAS"
            or "PCT_MD_UND";
    }

    private static bool HasDimensionHint(string normalizedName)
        => DimensionNameHints.Any(hint => normalizedName.Contains(hint, StringComparison.OrdinalIgnoreCase));

    private static bool HasMetricHint(string normalizedName)
        => MetricNameHints.Any(hint => normalizedName.Contains(hint, StringComparison.OrdinalIgnoreCase));

    private static bool IsArtificialUiField(ReportFieldDefinition field)
    {
        if (!field.IsCalculated
            && field.SourceType is not ReportColumnSourceType.Calculated
            and not ReportColumnSourceType.RuleOutput
            and not ReportColumnSourceType.Unknown)
        {
            return false;
        }

        var normalized = NormalizeFieldKey(field.Key, field.SourceField);
        return normalized is "SEMAFORO"
            or "CHECK"
            or "ESTADO"
            or "STATUS"
            or "OBSERVACION"
            or "PREMIO_CALCULADO"
            or "AFECTACION_CALCULADA"
            or "REGLAS_GUIADAS_APLICADAS";
    }

    private IReadOnlyList<ReportFieldDefinition> LoadConfiguredFields()
    {
        var configured = new List<ReportFieldDefinition>();
        configured.AddRange(LoadConfiguredGroup("Dimensions", ReportColumnSourceType.Dimension, isMeasure: false, isDimension: true));
        configured.AddRange(LoadConfiguredGroup("Measures", ReportColumnSourceType.Measure, isMeasure: true, isDimension: false));
        configured.AddRange(LoadConfiguredGroup("Calculated", ReportColumnSourceType.Calculated, isMeasure: false, isDimension: false));

        var merged = new Dictionary<string, ReportFieldDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in configured)
        {
            var identity = BuildFieldIdentity(field);
            if (!merged.ContainsKey(identity))
                merged[identity] = CloneField(field);
        }

        foreach (var field in _biModelStructureService.GetFields())
        {
            var identity = BuildFieldIdentity(field);
            if (!merged.ContainsKey(identity))
                merged[identity] = CloneField(field);
        }

        if (merged.Count > 0)
        {
            return merged.Values
                .OrderBy(f => GetSourceOrder(f.SourceType))
                .ThenBy(f => f.DefaultOrder)
                .ThenBy(f => f.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        return BuildDefaultConfiguredFields();
    }

    private IReadOnlyList<ReportFieldDefinition> LoadConfiguredGroup(
        string groupName,
        ReportColumnSourceType sourceType,
        bool isMeasure,
        bool isDimension)
    {
        var result = new List<ReportFieldDefinition>();
        var section = _configuration.GetSection($"PowerBi:ReportBuilder:Fields:{groupName}");
        if (!section.Exists())
            return result;

        var order = 0;
        foreach (var item in section.GetChildren())
        {
            var sourceField = item["SourceField"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sourceField))
                continue;

            var key = item["Key"];
            var display = item["DisplayName"];
            var table = item["SourceTable"];
            var expression = item["DaxExpression"];
            var format = item["DefaultFormat"] ?? string.Empty;

            result.Add(new ReportFieldDefinition
            {
                Key = NormalizeFieldKey(key, sourceField),
                DisplayName = string.IsNullOrWhiteSpace(display) ? SimplifyName(sourceField) : display.Trim(),
                SourceTable = string.IsNullOrWhiteSpace(table) ? InferSourceTable(sourceField) : table.Trim(),
                SourceField = sourceField.Trim(),
                DaxExpression = string.IsNullOrWhiteSpace(expression) ? InferDaxExpression(sourceField, isMeasure) : expression.Trim(),
                SourceType = sourceType,
                DataType = ParseDataType(item["DataType"], isMeasure),
                IsMeasure = isMeasure,
                IsDimension = isDimension,
                IsCalculated = sourceType == ReportColumnSourceType.Calculated,
                DefaultFormat = format,
                AllowSorting = ParseBool(item["AllowSorting"], true),
                AllowFiltering = ParseBool(item["AllowFiltering"], !isMeasure),
                AllowRules = ParseBool(item["AllowRules"], true),
                DefaultOrder = order++
            });
        }

        return result;
    }

    private static IReadOnlyList<ReportFieldDefinition> BuildDefaultConfiguredFields()
    {
        var result = new List<ReportFieldDefinition>();
        var order = 0;

        result.Add(CreateField("VENDEDORES_COD_VEND", "COD_VEND", "VENDEDORES[COD_VEND]", ReportColumnSourceType.Dimension, ReportFieldDataType.Text, false, true, false, order++));
        result.Add(CreateField("MEDICIONES_DESCRIPCION", "DESCRIPCION", "MEDICIONES[DESCRIPCION]", ReportColumnSourceType.Dimension, ReportFieldDataType.Text, false, true, false, order++));
        result.Add(CreateField("MEDICIONES_CODIGO", "CODIGO", "MEDICIONES[CODIGO]", ReportColumnSourceType.Dimension, ReportFieldDataType.Text, false, true, false, order++));
        result.Add(CreateField("REPORTES_REPORTE", "REPORTE", "REPORTES[REPORTE]", ReportColumnSourceType.Dimension, ReportFieldDataType.Text, false, true, false, order++));

        result.Add(CreateMeasure("COBERTURA", "COBERTURA", order++, defaultFormat: "N0"));
        result.Add(CreateMeasure("CORDOBAS", "CORDOBAS", order++, defaultFormat: "N2"));
        result.Add(CreateMeasure("CAJAS", "CAJAS", order++, defaultFormat: "N2"));
        result.Add(CreateMeasure("UNIDADES", "UNIDADES", order++, defaultFormat: "N0"));
        result.Add(CreateMeasure("MD_CAJAS", "MD_CAJAS", order++, defaultFormat: "N2"));
        result.Add(CreateMeasure("MD_COB", "MD_COB", order++, defaultFormat: "N2"));
        result.Add(CreateMeasure("MD_COR", "MD_COR", order++, defaultFormat: "N2"));
        result.Add(CreateMeasure("MD_UNID", "MD_UNID", order++, defaultFormat: "N2"));
        result.Add(CreateMeasure("%MD_CAJAS", "%MD_CAJAS", order++, defaultFormat: "P2"));
        result.Add(CreateMeasure("%MD_COB", "%MD_COB", order++, defaultFormat: "P2"));
        result.Add(CreateMeasure("%MD_COR", "%MD_COR", order++, defaultFormat: "P2"));
        result.Add(CreateMeasure("%MD_UND", "%MD_UND", order++, defaultFormat: "P2"));

        return result;
    }

    private static IReadOnlyList<ReportFieldDefinition> BuildDefaultCalculatedFields()
    {
        return new List<ReportFieldDefinition>
        {
            CreateField("PREMIO_CALCULADO", "Premio calculado", "Premio Calculado", ReportColumnSourceType.Calculated, ReportFieldDataType.Decimal, true, false, true, 1000, "C2"),
            CreateField("AFECTACION_CALCULADA", "Afectacion", "Afectacion Calculada", ReportColumnSourceType.Calculated, ReportFieldDataType.Decimal, true, false, true, 1001, "C2"),
            CreateField("DIFERENCIA_META", "Diferencia vs meta", "Diferencia vs meta", ReportColumnSourceType.Calculated, ReportFieldDataType.Decimal, true, false, true, 1002, "N2")
        };
    }

    private static ReportFieldDefinition FromColumn(ReportColumnDefinition column)
    {
        var isMeasure = column.IsMeasure || column.SourceType == ReportColumnSourceType.Measure;
        return new ReportFieldDefinition
        {
            Key = NormalizeFieldKey(column.Key, column.SourceField),
            DisplayName = string.IsNullOrWhiteSpace(column.DisplayName) ? SimplifyName(column.SourceField) : column.DisplayName,
            SourceTable = string.IsNullOrWhiteSpace(column.SourceTable) ? InferSourceTable(column.SourceField) : column.SourceTable,
            SourceField = column.SourceField,
            DaxExpression = InferDaxExpression(column.SourceField, isMeasure),
            SourceType = column.SourceType,
            DataType = column.DataType,
            IsMeasure = isMeasure,
            IsDimension = column.IsDimension || column.SourceType == ReportColumnSourceType.Dimension,
            IsCalculated = column.IsCalculated || column.SourceType == ReportColumnSourceType.Calculated || column.SourceType == ReportColumnSourceType.RuleOutput,
            DefaultFormat = column.DefaultFormat ?? column.FormatString ?? string.Empty,
            AllowSorting = column.AllowSorting,
            AllowFiltering = column.AllowFiltering,
            AllowRules = column.AllowRules,
            DefaultOrder = column.Order,
            VisibleInColumnSelector = column.VisibleInColumnSelector,
            VisibleInAdvancedMode = column.VisibleInAdvancedMode,
            CanonicalKey = column.CatalogCanonicalKey,
            UsageCategory = Enum.TryParse<ReportFieldUsageCategory>(column.CatalogCategory, true, out var parsedCategory)
                ? parsedCategory
                : ReportFieldUsageCategory.Unknown
        };
    }

    private static ReportFieldDefinition CreateMeasure(string measureName, string displayName, int order, string defaultFormat = "N2")
    {
        return CreateField(
            NormalizeFieldKey(measureName, measureName),
            displayName,
            measureName,
            ReportColumnSourceType.Measure,
            ReportFieldDataType.Decimal,
            isMeasure: true,
            isDimension: false,
            isCalculated: false,
            order,
            defaultFormat);
    }

    private static ReportFieldDefinition CreateField(
        string key,
        string displayName,
        string sourceField,
        ReportColumnSourceType sourceType,
        ReportFieldDataType dataType,
        bool isMeasure,
        bool isDimension,
        bool isCalculated,
        int defaultOrder,
        string defaultFormat = "")
    {
        return new ReportFieldDefinition
        {
            Key = NormalizeFieldKey(key, sourceField),
            DisplayName = displayName,
            SourceTable = InferSourceTable(sourceField),
            SourceField = sourceField,
            DaxExpression = InferDaxExpression(sourceField, isMeasure),
            SourceType = sourceType,
            DataType = dataType,
            IsMeasure = isMeasure,
            IsDimension = isDimension,
            IsCalculated = isCalculated,
            DefaultFormat = defaultFormat,
            AllowSorting = true,
            AllowFiltering = isDimension,
            AllowRules = true,
            DefaultOrder = defaultOrder
        };
    }

    private static string BuildFieldIdentity(ReportFieldDefinition field)
    {
        var raw = string.IsNullOrWhiteSpace(field.SourceField)
            ? field.Key
            : field.SourceField;

        return NormalizeFieldKey(raw, raw);
    }

    private static ReportFieldDefinition CloneField(ReportFieldDefinition field)
    {
        return new ReportFieldDefinition
        {
            Key = field.Key,
            DisplayName = field.DisplayName,
            SourceTable = field.SourceTable,
            SourceField = field.SourceField,
            DaxExpression = field.DaxExpression,
            SourceType = field.SourceType,
            DataType = field.DataType,
            IsMeasure = field.IsMeasure,
            IsDimension = field.IsDimension,
            IsCalculated = field.IsCalculated,
            DefaultFormat = field.DefaultFormat,
            AllowSorting = field.AllowSorting,
            AllowFiltering = field.AllowFiltering,
            AllowRules = field.AllowRules,
            DefaultOrder = field.DefaultOrder,
            UsageCategory = field.UsageCategory,
            CanonicalKey = field.CanonicalKey,
            VisibleInColumnSelector = field.VisibleInColumnSelector,
            VisibleInRuleScopeSelector = field.VisibleInRuleScopeSelector,
            VisibleInRuleMetricSelector = field.VisibleInRuleMetricSelector,
            VisibleInAdvancedMode = field.VisibleInAdvancedMode
        };
    }

    private static string BuildSemanticFamily(ReportFieldDefinition field)
    {
        var source = !string.IsNullOrWhiteSpace(field.SourceField)
            ? field.SourceField
            : field.DisplayName;

        var name = SimplifyName(source);
        var normalized = NormalizeSourceName(name)
            .Replace("%", "PCT_", StringComparison.Ordinal);

        var chars = normalized
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '+' ? ch : '_')
            .ToArray();

        var family = new string(chars);
        while (family.Contains("__", StringComparison.Ordinal))
            family = family.Replace("__", "_", StringComparison.Ordinal);

        family = family.Trim('_');
        if (family.EndsWith("_VEND", StringComparison.OrdinalIgnoreCase))
            family = family[..^5];

        if (FamilyAliases.TryGetValue(family, out var alias))
            family = alias;

        return family;
    }

    private static string NormalizeSourceName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var noAccents = RemoveDiacritics(value);
        var chars = noAccents
            .Trim()
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '+' or '%')
            .Select(char.ToUpperInvariant)
            .ToArray();

        return new string(chars);
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var chars = normalized
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .ToArray();

        return new string(chars).Normalize(NormalizationForm.FormC);
    }

    private static int GetCategoryOrder(ReportFieldUsageCategory category)
    {
        return category switch
        {
            ReportFieldUsageCategory.Dimension => 0,
            ReportFieldUsageCategory.Metric => 1,
            ReportFieldUsageCategory.Hidden => 2,
            _ => 3
        };
    }

    private static string NormalizeFieldKey(string? key, string sourceField)
    {
        var value = string.IsNullOrWhiteSpace(key) ? sourceField : key;
        var chars = value
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '_')
            .ToArray();

        var normalized = new string(chars);
        return string.IsNullOrWhiteSpace(normalized) ? "FIELD" : normalized;
    }

    private static int GetSourceOrder(ReportColumnSourceType sourceType)
    {
        return sourceType switch
        {
            ReportColumnSourceType.Dimension => 0,
            ReportColumnSourceType.Measure => 1,
            ReportColumnSourceType.Calculated => 2,
            ReportColumnSourceType.RuleOutput => 3,
            _ => 4
        };
    }

    private static string InferSourceTable(string sourceField)
    {
        if (string.IsNullOrWhiteSpace(sourceField))
            return string.Empty;

        var openBracket = sourceField.IndexOf('[');
        if (openBracket <= 0)
            return string.Empty;

        return sourceField[..openBracket].Trim();
    }

    private static string InferDaxExpression(string sourceField, bool isMeasure)
    {
        if (string.IsNullOrWhiteSpace(sourceField))
            return string.Empty;

        if (sourceField.Contains('[', StringComparison.Ordinal) && sourceField.Contains(']', StringComparison.Ordinal))
            return sourceField.Trim();

        if (!isMeasure)
            return sourceField.Trim();

        return $"[{sourceField.Trim()}]";
    }

    private static string SimplifyName(string sourceField)
    {
        if (string.IsNullOrWhiteSpace(sourceField))
            return string.Empty;

        var open = sourceField.LastIndexOf('[');
        var close = sourceField.LastIndexOf(']');
        if (open >= 0 && close > open)
            return sourceField.Substring(open + 1, close - open - 1);

        return sourceField;
    }

    private static ReportFieldDataType ParseDataType(string? value, bool isMeasure)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            Enum.TryParse<ReportFieldDataType>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return isMeasure ? ReportFieldDataType.Decimal : ReportFieldDataType.Text;
    }

    private static bool ParseBool(string? value, bool defaultValue)
    {
        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }
}
