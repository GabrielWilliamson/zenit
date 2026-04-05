using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Zenit.Core.Infrastructure.PowerBi.Models;

namespace Zenit.Helpers;

public static class PowerBiDataTableMapper
{
    /// <summary>
    /// Convierte toda la tabla a DataTable.
    /// ⚠️ Si hay demasiadas filas, esto puede ser pesado para UI.
    /// En Reportes ahora preferimos usar la versión paginada.
    /// </summary>
    public static DataTable ToDataTable(PowerBiQueryTable table)
        => ToDataTable(table, skip: 0, take: int.MaxValue);

    /// <summary>
    /// Convierte SOLO un rango de filas (paginación).
    /// Esto evita congelar la app cuando el resultado trae miles de filas.
    /// </summary>
    public static DataTable ToDataTable(PowerBiQueryTable table, int skip, int take)
    {
        if (table == null) throw new ArgumentNullException(nameof(table));
        if (skip < 0) skip = 0;
        if (take <= 0) take = 0;

        // Determina orden estable de columnas:
        // 1) Preferimos 'columns' que envía Power BI
        // 2) Si no viene, inferimos de las llaves en rows
        var columnNames = (table.Columns.Count > 0)
            ? table.Columns.Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList()
            : table.Rows.SelectMany(r => r.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var dt = new DataTable(string.IsNullOrWhiteSpace(table.Name) ? "Result" : table.Name);

        // Mapa: nombre crudo -> nombre display único (sin prefijo TABLA[])
        var displayNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in columnNames)
        {
            var simplified = SimplifyColumnName(raw);
            var unique = EnsureUnique(simplified, used);
            displayNameMap[raw] = unique;

            dt.Columns.Add(unique, typeof(object));
        }

        var rows = table.Rows.Skip(skip).Take(take);

        foreach (var row in rows)
        {
            var dr = dt.NewRow();

            foreach (var raw in columnNames)
            {
                var col = displayNameMap[raw];
                row.TryGetValue(raw, out var value);
                dr[col] = value ?? DBNull.Value;
            }

            dt.Rows.Add(dr);
        }

        return dt;
    }

    private static string SimplifyColumnName(string name)
    {
        // Ej: "VENDEDORES[COD_VEND]" => "COD_VEND"
        // Ej: "MEDICIONES[DESCRIPCION]" => "DESCRIPCION"
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var open = name.LastIndexOf('[');
        var close = name.LastIndexOf(']');

        if (open >= 0 && close > open)
            return name.Substring(open + 1, close - open - 1);

        return name;
    }

    private static string EnsureUnique(string baseName, HashSet<string> used)
    {
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "COL";

        var candidate = baseName;
        var i = 2;

        while (!used.Add(candidate))
        {
            candidate = $"{baseName}_{i}";
            i++;
        }

        return candidate;
    }
}
