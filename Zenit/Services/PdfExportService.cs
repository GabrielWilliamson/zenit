using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Zenit.Core.Infrastructure.PowerBi.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Zenit.Services;

/// <summary>
/// Exporta resultados de Power BI (tablas) a PDF.
///
/// Tecnología recomendada para este proyecto:
/// - QuestPDF (100% .NET, sin dependencias de Office/Word/HTML, muy estable para tablas).
/// - Genera PDFs directamente a disco (Descargas) con nombre que incluye COD_VEND.
/// </summary>
public sealed class PdfExportService
{
    /// <summary>
    /// Exporta UNA tabla a PDF (un reporte).
    /// </summary>
    public string ExportPowerBiTableToDownloads(
        PowerBiQueryTable table,
        string reportTitle,
        string datasetName,
        string monthName,
        int year,
        string vendorCode,
        string? vendorName,
        string? grupoDisplay)
    {
        if (table == null) throw new ArgumentNullException(nameof(table));

        var downloads = GetDownloadsFolder();
        Directory.CreateDirectory(downloads);

        var safeVendor = SanitizeFilePart(vendorCode);
        var safeReport = SanitizeFilePart(reportTitle);
        var safeMonth = SanitizeFilePart(monthName);

        var fileName = $"{safeReport}_CODVEND_{safeVendor}_{safeMonth}_{year}.pdf";
        var fullPath = Path.Combine(downloads, fileName);

        var meta = new PdfMeta
        {
            ReportTitle = reportTitle,
            DatasetName = datasetName,
            MonthName = monthName,
            Year = year,
            VendorCode = vendorCode,
            VendorName = vendorName,
            Grupo = grupoDisplay
        };

        var doc = new PowerBiTableDocument(table, meta);
        doc.GeneratePdf(fullPath);

        return fullPath;
    }

    /// <summary>
    /// Exporta VARIOS reportes (tablas) dentro de UN SOLO PDF.
    /// Se usa para: "Generar todos los reportes en un solo PDF por vendedor".
    /// </summary>
    public string ExportMultiplePowerBiTablesToDownloads(
        IReadOnlyList<(string ReportTitle, PowerBiQueryTable Table)> sections,
        string combinedTitle,
        string datasetName,
        string monthName,
        int year,
        string vendorCode,
        string? vendorName,
        string? grupoDisplay)
    {
        if (sections == null) throw new ArgumentNullException(nameof(sections));
        if (sections.Count == 0) throw new ArgumentException("Debe incluir al menos un reporte.", nameof(sections));

        var downloads = GetDownloadsFolder();
        Directory.CreateDirectory(downloads);

        var safeVendor = SanitizeFilePart(vendorCode);
        var safeMonth = SanitizeFilePart(monthName);
        var safeTitle = SanitizeFilePart(combinedTitle);

        var fileName = $"{safeTitle}_CODVEND_{safeVendor}_{safeMonth}_{year}.pdf";
        var fullPath = Path.Combine(downloads, fileName);

        var meta = new PdfMeta
        {
            ReportTitle = combinedTitle,
            DatasetName = datasetName,
            MonthName = monthName,
            Year = year,
            VendorCode = vendorCode,
            VendorName = vendorName,
            Grupo = grupoDisplay
        };

        var doc = new PowerBiMultiTableDocument(sections, meta);
        doc.GeneratePdf(fullPath);

        return fullPath;
    }

    private static string GetDownloadsFolder()
    {
        // Funciona para apps unpackaged y también dentro de MSIX (si tiene acceso al perfil de usuario).
        // Si el sistema no tiene carpeta "Downloads" (muy raro), cae a Desktop.
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloads = Path.Combine(user, "Downloads");
        if (Directory.Exists(downloads))
            return downloads;

        return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }

    private static string SanitizeFilePart(string? value)
    {
        value ??= "";
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Where(ch => !invalid.Contains(ch)).ToArray());
        cleaned = cleaned.Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "NA" : cleaned;
    }

    private sealed class PdfMeta
    {
        public string ReportTitle { get; set; } = string.Empty;
        public string DatasetName { get; set; } = string.Empty;
        public string MonthName { get; set; } = string.Empty;
        public int Year { get; set; }
        public string VendorCode { get; set; } = string.Empty;
        public string? VendorName { get; set; }
        public string? Grupo { get; set; }
    }

    private sealed class PowerBiTableDocument : IDocument
    {
        private readonly PowerBiQueryTable _table;
        private readonly PdfMeta _meta;

        public PowerBiTableDocument(PowerBiQueryTable table, PdfMeta meta)
        {
            _table = table;
            _meta = meta;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container
                .Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(20);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    page.Header().Element(ComposeHeader);
                    page.Content().Element(ComposeContent);
                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Página ");
                        x.CurrentPageNumber();
                        x.Span(" / ");
                        x.TotalPages();
                    });
                });
        }

        private void ComposeHeader(IContainer container)
        {
            container.PaddingBottom(10)
                .Background(Colors.Grey.Lighten4)
                .Padding(10)
                .Border(1)
                .BorderColor(Colors.Grey.Lighten2)
                .Column(col =>
                {
                    col.Item().Text(_meta.ReportTitle).SemiBold().FontSize(16);

                    // Nota: no mostramos el nombre del Dataset en el PDF.
                    var vendorLine = !string.IsNullOrWhiteSpace(_meta.VendorName)
                        ? $"Vendedor: {_meta.VendorName}  |  COD_VEND: {_meta.VendorCode}"
                        : $"COD_VEND: {_meta.VendorCode}";

                    col.Item().Text(vendorLine).FontSize(10).SemiBold();
                    col.Item().Text($"Periodo: {_meta.MonthName} {_meta.Year}").FontSize(9);

                    if (!string.IsNullOrWhiteSpace(_meta.Grupo) && !string.Equals(_meta.Grupo, "Todos", StringComparison.OrdinalIgnoreCase))
                        col.Item().Text($"GRUPO: {_meta.Grupo}").FontSize(9);

                    col.Item().PaddingTop(6).LineHorizontal(1);
                });
        }

        private void ComposeContent(IContainer container)
        {
            if (_table.Rows.Count == 0)
            {
                container.PaddingTop(20).Text("Sin resultados");
                return;
            }

            // Power BI a veces NO incluye "columns" en la respuesta de ExecuteQueries.
            // En ese caso inferimos las columnas desde las keys del primer row.
            List<string> columnNames;
            if (_table.Columns.Count > 0)
            {
                columnNames = _table.Columns
                    .Select(c => c.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();
            }
            else
            {
                columnNames = _table.Rows[0].Keys
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .ToList();
            }

            if (columnNames.Count == 0)
            {
                container.PaddingTop(20).Text("Sin resultados");
                return;
            }

            // Evitamos crash si hay columnas repetidas: usamos índice.
            var cols = columnNames.Select((n, i) => new { Index = i, Name = n }).ToList();

            // Regla: no incluir filas sin metas (todas las columnas MD_* en 0 / vacío)
            var metaCols = cols
                .Select(c => new { c.Name, Norm = NormalizeColumn(c.Name) })
                .Where(x => x.Norm.StartsWith("MD_", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Name)
                .ToList();

            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    foreach (var _ in cols)
                        columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    foreach (var c in cols)
                    {
                        header.Cell().Element(CellHeaderStyle).Text(NiceHeader(c.Name));
                    }
                });

                foreach (var row in _table.Rows)
                {
                    if (ShouldSkipRowBecauseNoMeta(row, metaCols))
                        continue;

                    foreach (var c in cols)
                    {
                        var raw = GetRawValue(row, c.Name);
                        table.Cell().Element(CellBodyStyle).Element(cell => ComposeCell(cell, c.Name, raw));
                    }
                }
            });
        }

        private static object? GetRawValue(Dictionary<string, object?> row, string columnName)
        {
            if (row.TryGetValue(columnName, out var v))
                return v;
            return null;
        }

        internal static void ComposeCell(IContainer container, string columnName, object? raw)
        {
            var text = FormatCellText(columnName, raw, out var tone);

            container.Text(t =>
            {
                var span = t.Span(text);
                span.FontSize(9);

                if (tone == CellTone.Good)
                    span.FontColor(Colors.Green.Darken2);
                else if (tone == CellTone.Bad)
                    span.FontColor(Colors.Red.Darken2);
            });
        }

        private enum CellTone { Neutral, Good, Bad }

        private static string FormatCellText(string columnName, object? raw, out CellTone tone)
        {
            tone = CellTone.Neutral;

            if (raw is null)
                return string.Empty;

            // Porcentajes
            if (LooksLikePercentColumn(columnName))
            {
                if (TryGetDouble(raw, out var v))
                {
                    var pct = Math.Abs(v) <= 1.5 ? v * 100.0 : v;

                    if (pct >= 100.0)
                    {
                        tone = CellTone.Good;
                        return $"✔ {pct:0.0}%";
                    }

                    if (pct < 0.0)
                    {
                        tone = CellTone.Bad;
                        return $"▼ {pct:0.0}%";
                    }

                    return $"{pct:0.0}%";
                }

                return raw.ToString() ?? string.Empty;
            }

            // Números negativos en rojo
            if (TryGetDouble(raw, out var num))
            {
                if (num < 0)
                {
                    tone = CellTone.Bad;
                    return $"▼ {num:N2}";
                }

                return num % 1 == 0 ? $"{num:N0}" : $"{num:N2}";
            }

            return raw switch
            {
                DateTime dt => dt.ToString("yyyy-MM-dd"),
                _ => raw.ToString() ?? string.Empty
            };
        }

        private static bool TryGetDouble(object raw, out double value)
        {
            switch (raw)
            {
                case decimal d: value = (double)d; return true;
                case double d: value = d; return true;
                case float f: value = f; return true;
                case int i: value = i; return true;
                case long l: value = l; return true;
                case short s: value = s; return true;
                case byte b: value = b; return true;
                case string s:
                    s = s.Trim();
                    if (double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value)) return true;
                    if (double.TryParse(s, out value)) return true;
                    value = 0; return false;
                default:
                    value = 0; return false;
            }
        }

        internal static string NormalizeColumn(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            return name.Trim().Trim('[', ']').Replace(" ", string.Empty);
        }

        internal static string NiceHeader(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            // Limpia encabezados tipo "VENDEDORES[COD_VEND]" o "[MD_COB]"
            var s = name.Trim();
            if (s.Contains('[') && s.Contains(']'))
            {
                var i1 = s.IndexOf('[');
                var i2 = s.LastIndexOf(']');
                if (i2 > i1)
                    s = s.Substring(i1 + 1, i2 - i1 - 1);
            }

            return s;
        }

        internal static bool ShouldSkipRowBecauseNoMeta(Dictionary<string, object?> row, List<string> metaColumns)
        {
            if (metaColumns.Count == 0) return false;

            bool IsZeroish(object? v)
            {
                if (v is null) return true;
                var s = v.ToString()?.Trim();
                if (string.IsNullOrEmpty(s)) return true;
                if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
                    return d == 0m;
                if (decimal.TryParse(s, out d))
                    return d == 0m;
                return false;
            }

            return metaColumns.All(col =>
            {
                row.TryGetValue(col, out var v);
                return IsZeroish(v);
            });
        }

        internal static string GetCellValue(Dictionary<string, object?> row, string columnName)
        {
            if (!row.TryGetValue(columnName, out var value) || value == null)
                return string.Empty;

            // Formato para porcentajes: columnas tipo "%" o "[%MD_COR]", etc.
            // Power BI suele devolver porcentajes como decimales (0.11 => 11%).
            if (LooksLikePercentColumn(columnName))
                return FormatPercent(value);

            return value switch
            {
                DateTime dt => dt.ToString("yyyy-MM-dd"),
                decimal dec => dec.ToString("N2"),
                double dbl => dbl.ToString("N2"),
                float fl => fl.ToString("N2"),
                _ => value.ToString() ?? string.Empty
            };
        }

        private static bool LooksLikePercentColumn(string columnName)
        {
            if (string.IsNullOrWhiteSpace(columnName)) return false;
            var name = columnName.Trim();
            if (name.Contains('%')) return true;
            name = name.ToUpperInvariant();
            return name.Contains("PORC") || name.Contains("PCT") || name.Contains("PERCENT");
        }

        private static string FormatPercent(object value)
        {
            double? num = value switch
            {
                decimal d => (double)d,
                double d => d,
                float f => f,
                int i => i,
                long l => l,
                short s => s,
                _ => null
            };

            if (num is null)
                return value.ToString() ?? string.Empty;

            var v = num.Value;
            var abs = Math.Abs(v);
            var pct = abs <= 1.5 ? v * 100.0 : v;
            return $"{pct:0.0}%";
        }

        internal static IContainer CellHeaderStyle(IContainer container)
        {
            return container
                .Background(Colors.Blue.Lighten4)
                .Border(1)
                .BorderColor(Colors.Grey.Lighten1)
                .PaddingVertical(5)
                .PaddingHorizontal(7)
                .DefaultTextStyle(x => x.SemiBold().FontSize(9));
        }

        internal static IContainer CellBodyStyle(IContainer container)
        {
            return container
                .Border(1)
                .BorderColor(Colors.Grey.Lighten2)
                .PaddingVertical(3)
                .PaddingHorizontal(6);
        }
    }

    private sealed class PowerBiMultiTableDocument : IDocument
    {
        private readonly IReadOnlyList<(string ReportTitle, PowerBiQueryTable Table)> _sections;
        private readonly PdfMeta _meta;

        public PowerBiMultiTableDocument(IReadOnlyList<(string ReportTitle, PowerBiQueryTable Table)> sections, PdfMeta meta)
        {
            _sections = sections;
            _meta = meta;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(20);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Página ");
                    x.CurrentPageNumber();
                    x.Span(" / ");
                    x.TotalPages();
                });
            });
        }

        private void ComposeHeader(IContainer container)
        {
            container.PaddingBottom(10)
                .Background(Colors.Grey.Lighten4)
                .Padding(10)
                .Border(1)
                .BorderColor(Colors.Grey.Lighten2)
                .Column(col =>
                {
                    col.Item().Text(_meta.ReportTitle).SemiBold().FontSize(16);

                    var vendorLine = !string.IsNullOrWhiteSpace(_meta.VendorName)
                        ? $"Vendedor: {_meta.VendorName}  |  COD_VEND: {_meta.VendorCode}"
                        : $"COD_VEND: {_meta.VendorCode}";

                    col.Item().Text(vendorLine).FontSize(10).SemiBold();
                    col.Item().Text($"Periodo: {_meta.MonthName} {_meta.Year}").FontSize(9);

                    if (!string.IsNullOrWhiteSpace(_meta.Grupo) && !string.Equals(_meta.Grupo, "Todos", StringComparison.OrdinalIgnoreCase))
                        col.Item().Text($"GRUPO: {_meta.Grupo}").FontSize(9);

                    col.Item().PaddingTop(6).LineHorizontal(1);
                });
        }

        private void ComposeContent(IContainer container)
        {
            container.Column(col =>
            {
                for (int i = 0; i < _sections.Count; i++)
                {
                    var (title, table) = _sections[i];

                    col.Item().PaddingTop(i == 0 ? 0 : 14).Text(title).SemiBold().FontSize(12);

                    if (table == null || table.Rows.Count == 0)
                    {
                        col.Item().PaddingTop(6).Text("Sin resultados");
                    }
                    else
                    {
                        col.Item().PaddingTop(6).Element(c => ComposeOneTable(c, table));
                    }

                    if (i < _sections.Count - 1)
                        col.Item().PaddingTop(10).LineHorizontal(0.5f);
                }
            });
        }

        private static void ComposeOneTable(IContainer container, PowerBiQueryTable tableData)
        {
            List<string> columnNames;
            if (tableData.Columns.Count > 0)
            {
                columnNames = tableData.Columns
                    .Select(c => c.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();
            }
            else
            {
                columnNames = tableData.Rows[0].Keys
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .ToList();
            }

            if (columnNames.Count == 0)
            {
                container.Text("Sin resultados");
                return;
            }

            var cols = columnNames.Select((n, idx) => new { Index = idx, Name = n }).ToList();

            var metaCols = cols
                .Select(c => new { c.Name, Norm = PowerBiTableDocument.NormalizeColumn(c.Name) })
                .Where(x => x.Norm.StartsWith("MD_", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Name)
                .ToList();

            container.Table(t =>
            {
                t.ColumnsDefinition(columns =>
                {
                    foreach (var _ in cols)
                        columns.RelativeColumn();
                });

                t.Header(header =>
                {
                    foreach (var c in cols)
                        header.Cell().Element(PowerBiTableDocument.CellHeaderStyle).Text(PowerBiTableDocument.NiceHeader(c.Name));
                });

                foreach (var row in tableData.Rows)
                {
                    if (PowerBiTableDocument.ShouldSkipRowBecauseNoMeta(row, metaCols))
                        continue;

                    foreach (var c in cols)
                    {
                        var raw = row.TryGetValue(c.Name, out var v) ? v : null;
                        t.Cell().Element(PowerBiTableDocument.CellBodyStyle).Element(cell => PowerBiTableDocument.ComposeCell(cell, c.Name, raw));
                    }
                }
            });
        }
    }
}
