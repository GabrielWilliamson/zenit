using System.Data;
using System.Text.Json;
using Zenit.Core.Data;
using Zenit.Models.CustomReports;
using Microsoft.EntityFrameworkCore;

namespace Zenit.Services;

public sealed class ReportTemplateService
{
    private const string TableName = "custom_report_templates";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppDbContext _dbContext;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public ReportTemplateService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ReportTemplate>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (await HasTemplatesTableAsync(cancellationToken))
                return await ReadFromDatabaseAsync(cancellationToken);

            return await ReadFromFileAsync(cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<ReportTemplate> SaveAsync(ReportTemplate template, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            if (template.Id == Guid.Empty)
                template.Id = Guid.NewGuid();
            if (template.FechaCreacionUtc == default)
                template.FechaCreacionUtc = now;

            template.FechaActualizacionUtc = now;
            template.ColumnDesign = NormalizeColumnDesign(template);

            if (await HasTemplatesTableAsync(cancellationToken))
            {
                await SaveToDatabaseAsync(template, cancellationToken);
                return template;
            }

            var templates = await ReadInternalFileAsync(cancellationToken);
            var index = templates.FindIndex(t => t.Id == template.Id);
            if (index >= 0)
                templates[index] = Clone(template);
            else
                templates.Add(Clone(template));

            await WriteInternalFileAsync(templates, cancellationToken);
            return template;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task DeleteAsync(Guid templateId, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (await HasTemplatesTableAsync(cancellationToken))
            {
                await DeleteFromDatabaseAsync(templateId, cancellationToken);
                return;
            }

            var templates = await ReadInternalFileAsync(cancellationToken);
            templates.RemoveAll(t => t.Id == templateId);
            await WriteInternalFileAsync(templates, cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<ReportTemplate> DuplicateAsync(Guid templateId, string? newName = null, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var templates = (await GetAllInternalAsync(cancellationToken)).ToList();
            var existing = templates.FirstOrDefault(t => t.Id == templateId)
                ?? throw new InvalidOperationException("No se encontro la plantilla a duplicar.");

            var clone = Clone(existing);
            clone.Id = Guid.NewGuid();
            clone.Nombre = string.IsNullOrWhiteSpace(newName)
                ? $"{existing.Nombre} (Copia)"
                : newName.Trim();
            clone.FechaCreacionUtc = DateTime.UtcNow;
            clone.FechaActualizacionUtc = DateTime.UtcNow;

            if (await HasTemplatesTableAsync(cancellationToken))
            {
                await SaveToDatabaseAsync(clone, cancellationToken);
                return clone;
            }

            templates.Add(clone);
            await WriteInternalFileAsync(templates, cancellationToken);
            return clone;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<IReadOnlyList<ReportTemplate>> GetAllInternalAsync(CancellationToken cancellationToken)
    {
        if (await HasTemplatesTableAsync(cancellationToken))
            return await ReadFromDatabaseAsync(cancellationToken);

        return await ReadInternalFileAsync(cancellationToken);
    }

    private async Task<bool> HasTemplatesTableAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND table_name = @table_name
            );
            """;

        await _dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var cmd = _dbContext.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = sql;
            var parameter = cmd.CreateParameter();
            parameter.ParameterName = "@table_name";
            parameter.Value = TableName;
            cmd.Parameters.Add(parameter);

            var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
            return scalar is true || (scalar is bool b && b);
        }
        finally
        {
            await _dbContext.Database.CloseConnectionAsync();
        }
    }

    private async Task<IReadOnlyList<ReportTemplate>> ReadFromDatabaseAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, name, source, column_design, rules, created_at, updated_at
            FROM custom_report_templates
            ORDER BY updated_at DESC, created_at DESC;
            """;

        var templates = new List<ReportTemplate>();
        await _dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var cmd = _dbContext.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = sql;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetGuid(reader.GetOrdinal("id"));
                var name = reader.GetString(reader.GetOrdinal("name"));
                var source = reader.GetString(reader.GetOrdinal("source"));
                var columnDesign = reader.GetString(reader.GetOrdinal("column_design"));
                var rulesText = reader.IsDBNull(reader.GetOrdinal("rules"))
                    ? "[]"
                    : reader.GetString(reader.GetOrdinal("rules"));
                var createdAt = reader.GetDateTime(reader.GetOrdinal("created_at"));
                var updatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"));

                var template = new ReportTemplate
                {
                    Id = id,
                    Nombre = name,
                    TipoReporte = source,
                    ReporteOrigen = source,
                    ColumnDesign = columnDesign,
                    Ruth = ParseRuth(rulesText),
                    FechaCreacionUtc = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc),
                    FechaActualizacionUtc = DateTime.SpecifyKind(updatedAt, DateTimeKind.Utc)
                };

                template.Columnas = BuildColumnsFromDesign(template.ColumnDesign);
                templates.Add(template);
            }
        }
        finally
        {
            await _dbContext.Database.CloseConnectionAsync();
        }

        return templates;
    }

    private async Task SaveToDatabaseAsync(ReportTemplate template, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO custom_report_templates (id, name, source, column_design, rules, created_at, updated_at)
            VALUES (@id, @name, @source, @column_design, @rules::jsonb, @created_at, @updated_at)
            ON CONFLICT (id)
            DO UPDATE SET
                name = EXCLUDED.name,
                source = EXCLUDED.source,
                column_design = EXCLUDED.column_design,
                rules = EXCLUDED.rules,
                updated_at = EXCLUDED.updated_at;
            """;

        var source = string.IsNullOrWhiteSpace(template.ReporteOrigen) ? template.TipoReporte : template.ReporteOrigen;
        var rulesJson = SerializeRulesPayload(template.Ruth);

        await _dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var cmd = _dbContext.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = sql;
            AddParameter(cmd, "@id", template.Id);
            AddParameter(cmd, "@name", template.Nombre.Trim());
            AddParameter(cmd, "@source", source?.Trim() ?? string.Empty);
            AddParameter(cmd, "@column_design", template.ColumnDesign ?? string.Empty);
            AddParameter(cmd, "@rules", rulesJson);
            AddParameter(cmd, "@created_at", template.FechaCreacionUtc);
            AddParameter(cmd, "@updated_at", template.FechaActualizacionUtc);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await _dbContext.Database.CloseConnectionAsync();
        }
    }

    private async Task DeleteFromDatabaseAsync(Guid templateId, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM custom_report_templates WHERE id = @id;";

        await _dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var cmd = _dbContext.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = sql;
            AddParameter(cmd, "@id", templateId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await _dbContext.Database.CloseConnectionAsync();
        }
    }

    private static void AddParameter(IDbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string NormalizeColumnDesign(ReportTemplate template)
    {
        if (!string.IsNullOrWhiteSpace(template.ColumnDesign))
            return template.ColumnDesign.Trim();

        return string.Join(",",
            template.Columnas
                .OrderBy(c => c.Order)
                .Select(c => string.IsNullOrWhiteSpace(c.SourceField) ? c.Key : c.SourceField)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim()));
    }

    private static List<JsonElement> ParseRuth(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new List<JsonElement>();

        using var document = JsonDocument.Parse(input);
        if (document.RootElement.ValueKind == JsonValueKind.Object)
            return new List<JsonElement> { document.RootElement.Clone() };

        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return new List<JsonElement>();

        return document.RootElement
            .EnumerateArray()
            .Select(item => item.Clone())
            .ToList();
    }

    private static string SerializeRulesPayload(IReadOnlyList<JsonElement> rules)
    {
        if (rules.Count == 1
            && rules[0].ValueKind == JsonValueKind.Object
            && (rules[0].TryGetProperty("rules_general", out _) || rules[0].TryGetProperty("rules_tiered", out _)))
        {
            return rules[0].GetRawText();
        }

        return JsonSerializer.Serialize(rules);
    }

    private static List<ReportColumnDefinition> BuildColumnsFromDesign(string? columnDesign)
    {
        if (string.IsNullOrWhiteSpace(columnDesign))
            return new List<ReportColumnDefinition>();

        var columns = new List<ReportColumnDefinition>();
        var parts = columnDesign
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var i = 0; i < parts.Length; i++)
        {
            var key = parts[i].Trim();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            columns.Add(new ReportColumnDefinition
            {
                Key = key,
                DisplayName = key,
                SourceField = key,
                Order = i,
                IsVisible = true,
                VisibleInColumnSelector = true
            });
        }

        return columns;
    }

    private static ReportTemplate Clone(ReportTemplate template)
    {
        return new ReportTemplate
        {
            Id = template.Id,
            Nombre = template.Nombre,
            TipoReporte = template.TipoReporte,
            ReporteOrigenReal = template.ReporteOrigenReal,
            ColumnDesign = template.ColumnDesign,
            Ruth = template.Ruth.Select(r => r.Clone()).ToList(),
            Columnas = template.Columnas
                .Select(c => new ReportColumnDefinition
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
                })
                .ToList(),
            FechaCreacionUtc = template.FechaCreacionUtc,
            FechaActualizacionUtc = template.FechaActualizacionUtc
        };
    }

    private async Task<IReadOnlyList<ReportTemplate>> ReadFromFileAsync(CancellationToken cancellationToken)
    {
        var templates = await ReadInternalFileAsync(cancellationToken);
        return templates
            .OrderByDescending(t => t.FechaActualizacionUtc)
            .ToList();
    }

    private async Task<List<ReportTemplate>> ReadInternalFileAsync(CancellationToken cancellationToken)
    {
        var path = GetTemplatesFilePath();
        if (!File.Exists(path))
            return new List<ReportTemplate>();

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
            return new List<ReportTemplate>();

        return JsonSerializer.Deserialize<List<ReportTemplate>>(json, JsonOptions)
               ?? new List<ReportTemplate>();
    }

    private async Task WriteInternalFileAsync(List<ReportTemplate> templates, CancellationToken cancellationToken)
    {
        var path = GetTemplatesFilePath();
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(templates, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    private static string GetTemplatesFilePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Zenit", "reports", "templates.json");
    }
}
