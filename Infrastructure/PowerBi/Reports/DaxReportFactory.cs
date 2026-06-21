using System;
using System.Text;

namespace Zenit.Infrastructure.PowerBi.Reports;

/// <summary>
/// Fabrica de queries DAX para Power BI (endpoint ExecuteQueries).
///
/// 🧠 Idea clave:
/// SUMMARIZECOLUMNS acepta "filtros" como argumentos extra: TREATAS(...) y DATESBETWEEN(...)
/// Por eso armamos el query como texto, pero de forma controlada y reutilizable.
/// </summary>
public static class DaxReportFactory
{
    // Valores esperados en la tabla REPORTES[REPORTE]
    public const string ReportPlanIncentivoKc = "PLAN INCENTIVO KC";
    public const string ReportTa = "TA";
    public const string ReportFocos = "FOCOS";

    // Reportes adicionales (nombres en REPORTES[REPORTE])
    public const string ReportBicCategorias = "BIC_CATEGORIAS";
    public const string ReportSegBayer = "SEG_BAYER";
    public const string ReportSolCategorias = "SOL CATEGORIAS";

    /// <summary>
    /// Plan Incentivo Kimberly (KC).
    /// </summary>
    public static string BuildPlanIncentivoKc(
        int year,
        int month,
        DaxFilterValue? codVend = null,
        DaxFilterValue? grupo = null,
        string codVendColumn = "VENDEDORES[COD_VEND]",
        string grupoColumn = "VENDEDORES[GRUPO]")
    {
        // Resultado esperado: COD_VEND, DESCRIPCION, MD_COB, KC, %MD_KC
        var sb = new StringBuilder(1024);

        sb.AppendLine("EVALUATE");
        sb.AppendLine("FILTER(");
        sb.AppendLine("    SUMMARIZECOLUMNS(");
        sb.AppendLine("        VENDEDORES[COD_VEND],");
        sb.AppendLine("        MEDICIONES[DESCRIPCION],");
        sb.AppendLine();

        sb.AppendLine($"        TREATAS({ToDaxSet(ReportPlanIncentivoKc)}, REPORTES[REPORTE]),");
        AppendTreatAs(sb, codVend, codVendColumn);
        AppendTreatAs(sb, grupo, grupoColumn);
        AppendDateFilter(sb, year, month);

        sb.AppendLine("        \"MD_COB\", [MD_COB],");
        sb.AppendLine("        \"KC\", [kc],");
        sb.AppendLine("        \"%MD_KC\", DIVIDE([kc],[MD_COB])");
        sb.AppendLine("    ),");
        sb.AppendLine("    [kc] > 0 || [MD_COB] > 0");
        sb.AppendLine(")");
        sb.AppendLine("ORDER BY VENDEDORES[COD_VEND], MEDICIONES[DESCRIPCION]");

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Plan Incentivo Kimberly (Mayoristas).
    /// </summary>
    public static string BuildPlanIncentivoMayoristas(
        int year,
        int month,
        DaxFilterValue? codVend = null,
        DaxFilterValue? grupo = null,
        string codVendColumn = "VENDEDORES[COD_VEND]",
        string grupoColumn = "VENDEDORES[GRUPO]")
    {
        // Resultado esperado: COD_VEND, DESCRIPCION, MD_COR, CORDOBAS, %MD_COR
        var sb = new StringBuilder(1024);

        sb.AppendLine("EVALUATE");
        sb.AppendLine("FILTER(");
        sb.AppendLine("    SUMMARIZECOLUMNS(");
        sb.AppendLine("        VENDEDORES[COD_VEND],");
        sb.AppendLine("        MEDICIONES[DESCRIPCION],");
        sb.AppendLine();

        sb.AppendLine($"        TREATAS({ToDaxSet(ReportPlanIncentivoKc)}, REPORTES[REPORTE]),");
        AppendTreatAs(sb, codVend, codVendColumn);
        AppendTreatAs(sb, grupo, grupoColumn);
        AppendDateFilter(sb, year, month);

        sb.AppendLine("        \"MD_COR\", [MD_COR],");
        sb.AppendLine("        \"CORDOBAS\", [CORDOBAS],");
        sb.AppendLine("        \"%MD_COR\", DIVIDE([CORDOBAS],[MD_COR])");
        sb.AppendLine("    ),");
        sb.AppendLine("    [CORDOBAS] > 0 || [MD_COR] > 0");
        sb.AppendLine(")");
        sb.AppendLine("ORDER BY VENDEDORES[COD_VEND], MEDICIONES[DESCRIPCION]");

        return sb.ToString().Trim();
    }

    public static string BuildTa(
        int year,
        int month,
        DaxFilterValue? codVend = null,
        DaxFilterValue? grupo = null,
        string codVendColumn = "VENDEDORES[COD_VEND]",
        string grupoColumn = "VENDEDORES[GRUPO]")
        => BuildCoberturaByMedicion(ReportTa, year, month, codVend, grupo, codVendColumn, grupoColumn);

    public static string BuildFocos(
        int year,
        int month,
        DaxFilterValue? codVend = null,
        DaxFilterValue? grupo = null,
        string codVendColumn = "VENDEDORES[COD_VEND]",
        string grupoColumn = "VENDEDORES[GRUPO]")
        => BuildCoberturaByMedicion(ReportFocos, year, month, codVend, grupo, codVendColumn, grupoColumn);

    public static string BuildBicCategorias(
        int year,
        int month,
        DaxFilterValue? codVend = null,
        DaxFilterValue? grupo = null,
        string codVendColumn = "VENDEDORES[COD_VEND]",
        string grupoColumn = "VENDEDORES[GRUPO]")
        => BuildCorByMedicion(new[] { ReportBicCategorias, "BIC CATEGORIAS" }, year, month, codVend, grupo, codVendColumn, grupoColumn);

    public static string BuildSolCategorias(
        int year,
        int month,
        DaxFilterValue? codVend = null,
        DaxFilterValue? grupo = null,
        string codVendColumn = "VENDEDORES[COD_VEND]",
        string grupoColumn = "VENDEDORES[GRUPO]")
        => BuildCorByMedicion(new[] { ReportSolCategorias, "SOL_CATEGORIAS" }, year, month, codVend, grupo, codVendColumn, grupoColumn);

    public static string BuildSegBayer(
        int year,
        int month,
        DaxFilterValue? codVend = null,
        DaxFilterValue? grupo = null,
        string codVendColumn = "VENDEDORES[COD_VEND]",
        string grupoColumn = "VENDEDORES[GRUPO]")
        => BuildCorYCobByMedicion(new[] { ReportSegBayer, "SEG BAYER" }, year, month, codVend, grupo, codVendColumn, grupoColumn);

    /// <summary>
    /// Query generica por nombre de reporte usando REPORTES[REPORTE].
    /// Se usa cuando el reporte no cae en los tipos predefinidos.
    /// </summary>
    public static string BuildByReporteName(
        string reporteName,
        int year,
        int month,
        DaxFilterValue? codVend = null,
        DaxFilterValue? grupo = null,
        string codVendColumn = "VENDEDORES[COD_VEND]",
        string grupoColumn = "VENDEDORES[GRUPO]")
    {
        if (string.IsNullOrWhiteSpace(reporteName))
            throw new ArgumentException("reporteName es requerido");

        var sb = new StringBuilder(1500);

        sb.AppendLine("EVALUATE");
        sb.AppendLine("FILTER(");
        sb.AppendLine("    SUMMARIZECOLUMNS(");
        sb.AppendLine("        VENDEDORES[COD_VEND],");
        sb.AppendLine("        MEDICIONES[DESCRIPCION],");
        sb.AppendLine();
        sb.AppendLine($"        TREATAS({ToDaxSet(reporteName.Trim())}, REPORTES[REPORTE]),");

        AppendTreatAs(sb, codVend, codVendColumn);
        AppendTreatAs(sb, grupo, grupoColumn);
        AppendDateFilter(sb, year, month);

        sb.AppendLine("        \"MD_COB\", [MD_COB],");
        sb.AppendLine("        \"COBERTURA\", [COBERTURA],");
        sb.AppendLine("        \"%MD_COB\", DIVIDE([COBERTURA],[MD_COB]),");
        sb.AppendLine("        \"MD_COR\", [MD_COR],");
        sb.AppendLine("        \"CORDOBAS\", [CORDOBAS],");
        sb.AppendLine("        \"%MD_COR\", DIVIDE([CORDOBAS],[MD_COR]),");
        sb.AppendLine("        \"KC\", [kc],");
        sb.AppendLine("        \"%MD_KC\", DIVIDE([kc],[MD_COB])");
        sb.AppendLine("    ),");
        sb.AppendLine("    [MD_COB] > 0 || [COBERTURA] > 0 || [MD_COR] > 0 || [CORDOBAS] > 0 || [KC] > 0");
        sb.AppendLine(")");
        sb.AppendLine("ORDER BY MEDICIONES[DESCRIPCION], VENDEDORES[COD_VEND]");

        return sb.ToString().Trim();
    }

    private static string BuildCorByMedicion(
        string[] reporteAliases,
        int year,
        int month,
        DaxFilterValue? codVend,
        DaxFilterValue? grupo,
        string codVendColumn,
        string grupoColumn)
    {
        // Resultado esperado: COD_VEND, DESCRIPCION, MD_COR, CORDOBAS, %MD_COR
        var sb = new StringBuilder(1024);

        sb.AppendLine("EVALUATE");
        sb.AppendLine("FILTER(");
        sb.AppendLine("    SUMMARIZECOLUMNS(");
        sb.AppendLine("        VENDEDORES[COD_VEND],");
        sb.AppendLine("        MEDICIONES[DESCRIPCION],");
        sb.AppendLine();
        sb.AppendLine($"        TREATAS({ToDaxSet(reporteAliases)}, REPORTES[REPORTE]),");

        AppendTreatAs(sb, codVend, codVendColumn);
        AppendTreatAs(sb, grupo, grupoColumn);
        AppendDateFilter(sb, year, month);

        sb.AppendLine("        \"MD_COR\", [MD_COR],");
        sb.AppendLine("        \"CORDOBAS\", [CORDOBAS],");
        sb.AppendLine("        \"%MD_COR\", DIVIDE([CORDOBAS],[MD_COR])");
        sb.AppendLine("    ),");
        sb.AppendLine("    [CORDOBAS] > 0 || [MD_COR] > 0");
        sb.AppendLine(")");
        sb.AppendLine("ORDER BY MEDICIONES[DESCRIPCION], VENDEDORES[COD_VEND]");

        return sb.ToString().Trim();
    }

    private static string BuildCorYCobByMedicion(
        string[] reporteAliases,
        int year,
        int month,
        DaxFilterValue? codVend,
        DaxFilterValue? grupo,
        string codVendColumn,
        string grupoColumn)
    {
        // Resultado esperado: COD_VEND, DESCRIPCION, MD_COR, CORDOBAS, %MD_COR, MD_COB, COBERTURA, %MD_COB
        var sb = new StringBuilder(1400);

        sb.AppendLine("EVALUATE");
        sb.AppendLine("FILTER(");
        sb.AppendLine("    SUMMARIZECOLUMNS(");
        sb.AppendLine("        VENDEDORES[COD_VEND],");
        sb.AppendLine("        MEDICIONES[DESCRIPCION],");
        sb.AppendLine();
        sb.AppendLine($"        TREATAS({ToDaxSet(reporteAliases)}, REPORTES[REPORTE]),");

        AppendTreatAs(sb, codVend, codVendColumn);
        AppendTreatAs(sb, grupo, grupoColumn);
        AppendDateFilter(sb, year, month);

        sb.AppendLine("        \"MD_COR\", [MD_COR],");
        sb.AppendLine("        \"CORDOBAS\", [CORDOBAS],");
        sb.AppendLine("        \"%MD_COR\", DIVIDE([CORDOBAS],[MD_COR]),");
        sb.AppendLine("        \"MD_COB\", [MD_COB],");
        sb.AppendLine("        \"COBERTURA\", [COBERTURA],");
        sb.AppendLine("        \"%MD_COB\", DIVIDE([COBERTURA],[MD_COB])");
        sb.AppendLine("    ),");
        sb.AppendLine("    [CORDOBAS] > 0 || [MD_COR] > 0 || [COBERTURA] > 0 || [MD_COB] > 0");
        sb.AppendLine(")");
        sb.AppendLine("ORDER BY MEDICIONES[DESCRIPCION], VENDEDORES[COD_VEND]");

        return sb.ToString().Trim();
    }

    private static string BuildCoberturaByMedicion(
        string reporte,
        int year,
        int month,
        DaxFilterValue? codVend,
        DaxFilterValue? grupo,
        string codVendColumn,
        string grupoColumn)
    {
        // Resultado esperado: COD_VEND, DESCRIPCION, MD_COB, COBERTURA, %MD_COB
        var sb = new StringBuilder(1024);

        sb.AppendLine("EVALUATE");
        sb.AppendLine("FILTER(");
        sb.AppendLine("    SUMMARIZECOLUMNS(");
        sb.AppendLine("        VENDEDORES[COD_VEND],");
        sb.AppendLine("        MEDICIONES[DESCRIPCION],");
        sb.AppendLine();
        sb.AppendLine($"        TREATAS({ToDaxSet(reporte)}, REPORTES[REPORTE]),");

        AppendTreatAs(sb, codVend, codVendColumn);
        AppendTreatAs(sb, grupo, grupoColumn);
        AppendDateFilter(sb, year, month);

        sb.AppendLine("        \"MD_COB\", [MD_COB],");
        sb.AppendLine("        \"COBERTURA\", [COBERTURA],");
        sb.AppendLine("        \"%MD_COB\", DIVIDE([COBERTURA],[MD_COB])");
        sb.AppendLine("    ),");
        sb.AppendLine("    [COBERTURA] > 0 || [MD_COB] > 0");
        sb.AppendLine(")");
        sb.AppendLine("ORDER BY MEDICIONES[DESCRIPCION]");

        return sb.ToString().Trim();
    }

    private static void AppendDateFilter(StringBuilder sb, int year, int month)
    {
        sb.AppendLine("        DATESBETWEEN(");
        sb.AppendLine("            TIEMPO[fecha],");
        sb.AppendLine($"            DATE({year},{month},1),");
        sb.AppendLine($"            EOMONTH(DATE({year},{month},1),0)");
        sb.AppendLine("        ),");
        sb.AppendLine();
    }

    /// <summary>
    /// Aplica un filtro a una columna usando TREATAS({valor}, Tabla[Columna]).
    ///
    /// ✔️ Lo más importante para tu caso:
    /// - NO "inventamos" si es texto o número.
    /// - El valor viene desde el dataset (ExecuteQueries) y ya trae su tipo real.
    ///   Ej: si VENDEDORES[COD_VEND] es texto, el valor será "018" (string).
    ///       si es número, el valor será 18 (number).
    /// - Eso evita el error clásico Text vs Integer, y también respeta ceros a la izquierda.
    /// </summary>
    private static void AppendTreatAs(StringBuilder sb, DaxFilterValue? filter, string columnRef)
    {
        if (filter is null)
            return;

        if (string.IsNullOrWhiteSpace(columnRef))
            throw new ArgumentException("columnRef es requerido");

        sb.AppendLine($"        TREATAS({{{filter.DaxLiteral}}}, {columnRef}),");
    }

    private static string ToDaxSet(string value)
        => ToDaxSet(new[] { value });

    private static string ToDaxSet(string[] values)
    {
        // Devuelve: {"A","B","C"}
        if (values == null || values.Length == 0)
            return "{}";

        var sb = new StringBuilder(128);
        sb.Append('{');
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('"').Append(Escape(values[i])).Append('"');
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string Escape(string value)
        => value.Replace("\"", "\"\"");
}
