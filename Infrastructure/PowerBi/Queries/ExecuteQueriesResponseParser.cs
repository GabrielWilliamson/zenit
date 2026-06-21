using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Zenit.Infrastructure.PowerBi.Models;

namespace Zenit.Infrastructure.PowerBi.Queries;

/// <summary>
/// Parser ligero para la respuesta de Power BI ExecuteQueries:
/// https://learn.microsoft.com/power-bi/developer/embedded/embed-service-principal#datasets-executequeries
/// </summary>
public sealed class ExecuteQueriesResponseParser
{
    public PowerBiQueryTable ParseFirstTable(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON response vacío");

        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
            throw new InvalidOperationException("Respuesta inválida: no existe 'results'");

        var firstResult = results[0];

        if (!firstResult.TryGetProperty("tables", out var tables) || tables.ValueKind != JsonValueKind.Array || tables.GetArrayLength() == 0)
            throw new InvalidOperationException("Respuesta inválida: no existe 'tables'");

        var firstTable = tables[0];

        var table = new PowerBiQueryTable
        {
            Name = firstTable.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty
        };

        // Columns (opcional en algunas respuestas)
        List<PowerBiQueryColumn> columns = new();
        if (firstTable.TryGetProperty("columns", out var columnsEl) && columnsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in columnsEl.EnumerateArray())
            {
                var colName = c.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                var dataType = c.TryGetProperty("dataType", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                columns.Add(new PowerBiQueryColumn { Name = colName, DataType = dataType });
            }
        }

        table.Columns.AddRange(columns);

        if (!firstTable.TryGetProperty("rows", out var rowsEl) || rowsEl.ValueKind != JsonValueKind.Array)
            return table; // sin filas

        foreach (var rowEl in rowsEl.EnumerateArray())
        {
            if (rowEl.ValueKind != JsonValueKind.Object)
                continue;

            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            // Si no tenemos columnas, tomamos las propiedades del objeto
            if (columns.Count == 0)
            {
                foreach (var prop in rowEl.EnumerateObject())
                {
                    row[prop.Name] = ReadElement(prop.Value, string.Empty);
                }
            }
            else
            {
                foreach (var col in columns)
                {
                    if (string.IsNullOrWhiteSpace(col.Name))
                        continue;

                    if (rowEl.TryGetProperty(col.Name, out var valueEl))
                    {
                        row[col.Name] = ReadElement(valueEl, col.DataType);
                    }
                    else
                    {
                        row[col.Name] = null;
                    }
                }
            }

            table.Rows.Add(row);
        }

        return table;
    }

    private static object? ReadElement(JsonElement el, string dataType)
    {
        if (el.ValueKind == JsonValueKind.Null || el.ValueKind == JsonValueKind.Undefined)
            return null;

        // Power BI suele enviar numbers para int/decimal y strings para datetime
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                return el.GetString();

            case JsonValueKind.True:
            case JsonValueKind.False:
                return el.GetBoolean();

            case JsonValueKind.Number:
                // Intentamos respetar el dataType, pero si no viene lo dejamos como double
                if (!string.IsNullOrWhiteSpace(dataType) &&
                    dataType.Contains("int", StringComparison.OrdinalIgnoreCase))
                {
                    if (el.TryGetInt64(out var l))
                        return l;
                }

                if (el.TryGetDouble(out var d))
                    return d;

                // fallback
                return el.ToString();

            case JsonValueKind.Object:
            case JsonValueKind.Array:
                return el.ToString();

            default:
                return el.ToString();
        }
    }
}
