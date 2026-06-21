using System.Globalization;
using System.Text;
using Zenit.Models.CustomReports;
using Zenit.Infrastructure.Configuration;
using Zenit.Infrastructure.Logging;

namespace Zenit.Services;

/// <summary>
/// Carga metadata de columnas y medidas reales del modelo BI desde un archivo
/// de estructura tabular (ej. Estructura BI.txt). Se usa como respaldo para
/// completar catalogos cuando la inferencia por muestra del reporte no alcanza.
/// </summary>
public sealed class BiModelStructureService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<BiModelStructureService> _logger;
    private readonly object _sync = new();
    private IReadOnlyList<ReportFieldDefinition> _cachedFields = Array.Empty<ReportFieldDefinition>();
    private string? _cachedPath;
    private DateTime _cachedWriteUtc;

    public BiModelStructureService(
        IConfiguration configuration,
        ILogger<BiModelStructureService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public IReadOnlyList<ReportFieldDefinition> GetFields()
    {
        var path = ResolveMetadataPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return Array.Empty<ReportFieldDefinition>();

        var writeUtc = File.GetLastWriteTimeUtc(path);

        lock (_sync)
        {
            if (string.Equals(path, _cachedPath, StringComparison.OrdinalIgnoreCase)
                && _cachedWriteUtc == writeUtc)
            {
                return CloneFields(_cachedFields);
            }
        }

        var parsed = ParseFields(path);

        lock (_sync)
        {
            _cachedPath = path;
            _cachedWriteUtc = writeUtc;
            _cachedFields = parsed;
        }

        return CloneFields(parsed);
    }

    private IReadOnlyList<ReportFieldDefinition> ParseFields(string path)
    {
        try
        {
            var lines = ReadLines(path)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            if (lines.Count < 3)
                return Array.Empty<ReportFieldDefinition>();

            var result = new List<ReportFieldDefinition>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var currentTable = string.Empty;
            var order = 0;

            for (var i = 0; i < lines.Count - 1; i++)
            {
                var name = lines[i];
                var kindRaw = lines[i + 1];
                var rowKind = ParseRowKind(kindRaw);

                if (rowKind == BiStructureRowKind.Unknown)
                {
                    if (IsHeaderToken(name))
                        continue;

                    continue;
                }

                i++;

                if (rowKind == BiStructureRowKind.Table)
                {
                    currentTable = name;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(name) || IsHeaderToken(name))
                    continue;

                var isMeasure = rowKind == BiStructureRowKind.Measure;
                var field = BuildField(currentTable, name, isMeasure, order++);
                var identity = NormalizeKey(string.IsNullOrWhiteSpace(field.SourceField) ? field.Key : field.SourceField);

                if (seen.Add(identity))
                    result.Add(field);
            }

            return result
                .OrderBy(field => field.SourceType == ReportColumnSourceType.Dimension ? 0 : 1)
                .ThenBy(field => field.DefaultOrder)
                .ThenBy(field => field.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo leer metadata BI desde {Path}.", path);
            return Array.Empty<ReportFieldDefinition>();
        }
    }

    private static ReportFieldDefinition BuildField(
        string sourceTable,
        string rawName,
        bool isMeasure,
        int order)
    {
        var cleanName = rawName.Trim();
        var normalized = NormalizeKey(cleanName);
        var sourceField = isMeasure || string.IsNullOrWhiteSpace(sourceTable)
            ? cleanName
            : $"{sourceTable.Trim()}[{cleanName}]";

        var dataType = InferDataType(cleanName, isMeasure);
        return new ReportFieldDefinition
        {
            Key = NormalizeKey(sourceField),
            DisplayName = cleanName,
            SourceTable = sourceTable.Trim(),
            SourceField = sourceField,
            DaxExpression = InferDaxExpression(sourceField, cleanName, isMeasure),
            SourceType = isMeasure ? ReportColumnSourceType.Measure : ReportColumnSourceType.Dimension,
            DataType = dataType,
            IsMeasure = isMeasure,
            IsDimension = !isMeasure,
            IsCalculated = false,
            DefaultFormat = InferDefaultFormat(normalized, isMeasure),
            AllowSorting = true,
            AllowFiltering = !isMeasure,
            AllowRules = true,
            DefaultOrder = order,
            UsageCategory = isMeasure ? ReportFieldUsageCategory.Metric : ReportFieldUsageCategory.Dimension,
            CanonicalKey = NormalizeKey(cleanName),
            VisibleInColumnSelector = true,
            VisibleInRuleScopeSelector = !isMeasure,
            VisibleInRuleMetricSelector = isMeasure,
            VisibleInAdvancedMode = false
        };
    }

    private static string InferDaxExpression(string sourceField, string cleanName, bool isMeasure)
    {
        if (!isMeasure)
            return sourceField;

        if (cleanName.StartsWith("[", StringComparison.Ordinal) && cleanName.EndsWith("]", StringComparison.Ordinal))
            return cleanName;

        return $"[{cleanName}]";
    }

    private static ReportFieldDataType InferDataType(string name, bool isMeasure)
    {
        var normalized = NormalizeKey(name);

        if (!isMeasure)
        {
            if (normalized.Contains("FECHA", StringComparison.OrdinalIgnoreCase))
                return ReportFieldDataType.Date;

            return ReportFieldDataType.Text;
        }

        if (normalized.Contains("DIAS", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("CONTADOR", StringComparison.OrdinalIgnoreCase))
        {
            return ReportFieldDataType.Integer;
        }

        return ReportFieldDataType.Decimal;
    }

    private static string InferDefaultFormat(string normalizedName, bool isMeasure)
    {
        if (!isMeasure)
            return string.Empty;

        if (normalizedName.StartsWith("_", StringComparison.Ordinal) || normalizedName.Contains("PORC", StringComparison.OrdinalIgnoreCase))
            return "P2";

        if (normalizedName.Contains("CORD", StringComparison.OrdinalIgnoreCase)
            || normalizedName.Contains("MONTO", StringComparison.OrdinalIgnoreCase)
            || normalizedName.Contains("COST", StringComparison.OrdinalIgnoreCase)
            || normalizedName.Contains("PRECIO", StringComparison.OrdinalIgnoreCase))
        {
            return "C2";
        }

        if (normalizedName.Contains("UNID", StringComparison.OrdinalIgnoreCase)
            || normalizedName.Contains("COBERTURA", StringComparison.OrdinalIgnoreCase)
            || normalizedName.Contains("DIAS", StringComparison.OrdinalIgnoreCase))
        {
            return "N0";
        }

        return "N2";
    }

    private static IReadOnlyList<string> ReadLines(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var utf8Text = Encoding.UTF8.GetString(bytes);
        var text = utf8Text;

        // Algunos archivos exportados de escritorio llegan en ANSI (Windows-1252).
        if (utf8Text.Count(ch => ch == '\u00C3') > 2)
        {
            var ansi = Encoding.GetEncoding(1252).GetString(bytes);
            if (ansi.Count(ch => ch == '\u00C3') < utf8Text.Count(ch => ch == '\u00C3'))
                text = ansi;
        }

        return text
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
            .ToList();
    }

    private string? ResolveMetadataPath()
    {
        var configuredPath = _configuration["PowerBi:ReportBuilder:BiStructureFilePath"];
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var candidates = new[]
        {
            configuredPath,
            Path.Combine(AppContext.BaseDirectory, "Estructura BI.txt"),
            Path.Combine(Environment.CurrentDirectory, "Estructura BI.txt"),
            Path.Combine(userProfile, "Downloads", "Estructura BI.txt"),
            Path.Combine(userProfile, "OneDrive", "Downloads", "Estructura BI.txt"),
            Path.Combine(userProfile, "OneDrive", "Descargas", "Estructura BI.txt")
        };

        return candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate));
    }

    private static bool IsHeaderToken(string value)
    {
        var normalized = NormalizeKey(value);
        return normalized is "NOMBRE" or "TIPO" or "DESCRIPCION";
    }

    private static BiStructureRowKind ParseRowKind(string value)
    {
        var normalized = NormalizeKey(RemoveDiacritics(value));
        return normalized switch
        {
            "TABLA" => BiStructureRowKind.Table,
            "COLUMNA" => BiStructureRowKind.Column,
            "MEDIDA" => BiStructureRowKind.Measure,
            _ => BiStructureRowKind.Unknown
        };
    }

    private static IReadOnlyList<ReportFieldDefinition> CloneFields(IReadOnlyList<ReportFieldDefinition> source)
    {
        return source
            .Select(field => new ReportFieldDefinition
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
            })
            .ToList();
    }

    private static string RemoveDiacritics(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalizedString = value.Normalize(NormalizationForm.FormD);
        var chars = normalizedString
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .ToArray();

        return new string(chars).Normalize(NormalizationForm.FormC);
    }

    private static string NormalizeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var chars = value
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '_')
            .ToArray();

        return new string(chars);
    }

    private enum BiStructureRowKind
    {
        Unknown,
        Table,
        Column,
        Measure
    }
}
