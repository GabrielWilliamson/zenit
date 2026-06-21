
using Zenit.Models.SalaryPlans.Entities;

namespace Zenit.Mappers;

public static class PowerBiRowMapper
{
    public static Medicion ToMedicion(Dictionary<string, System.Text.Json.JsonElement> row) => new()
    {
        Codigo = row.GetIntOrDefault("MEDICIONES[CODIGO]"),
        Descripcion = row.GetStringOrDefault("MEDICIONES[DESCRIPCION]")
    };

    public static MedicionMeta ToMedicionMeta(Dictionary<string, System.Text.Json.JsonElement> row) => new()
    {
        Codigo = row.GetIntOrDefault("MEDICIONES_METAS[CODIGO]"),
        TipoObjetoMeta = row.GetStringOrDefault("MEDICIONES_METAS[TIPO_OBJETO_META]"),
        CodigoObjetoMeta = row.GetIntOrDefault("MEDICIONES_METAS[CODIGO_OBJETO_META]"),
        FechaInicio = row.GetDateTimeOrNull("MEDICIONES_METAS[FECHA_INICIO]"),
        FechaFin = row.GetDateTimeOrNull("MEDICIONES_METAS[FECHA_FIN]"),
        MetaCordobas = row.GetDecimalOrDefault("MEDICIONES_METAS[META_CORDOBAS]"),
        MetaUnidades = row.GetIntOrDefault("MEDICIONES_METAS[META_UNIDADES]"),
        MetaCajas = row.GetDecimalOrDefault("MEDICIONES_METAS[META_CAJAS]"),
        MetaLitros = row.GetDecimalOrDefault("MEDICIONES_METAS[META_LITROS]"),
        MetaLibras = row.GetDecimalOrDefault("MEDICIONES_METAS[META_LIBRAS]"),
        MetaCobertura = row.GetIntOrDefault("MEDICIONES_METAS[META_COBERTURA]"),
        NombreReporte = row.GetStringOrDefault("MEDICIONES_METAS[NOMBRE_REPORTE]"),
        CodVend = row.GetStringOrDefault("MEDICIONES_METAS[COD_VEND]")
    };

    public static TiempoItem ToTiempoItem(Dictionary<string, System.Text.Json.JsonElement> row) => new()
    {
        Fecha = row.GetDateTimeOrNull("TIEMPO[fecha]") ?? DateTime.MinValue,
        FechaAnio = row.GetIntOrDefault("TIEMPO[Fecha_anio]"),
        FechaMes = row.GetIntOrDefault("TIEMPO[Fecha_Mes]"),
        DiaNumero = row.GetIntOrDefault("TIEMPO[Dia_Numero]"),
        Semestre = row.GetIntOrDefault("TIEMPO[Semestre]"),
        MesNombre = row.GetStringOrDefault("TIEMPO[Mes_Nombre]"),
        DiaNombre = row.GetStringOrDefault("TIEMPO[Dia_Nombre]"),
        MesYAnio = row.GetStringOrDefault("TIEMPO[Mes&Anio]"),
        Orden = row.GetStringOrDefault("TIEMPO[orden]")
    };

    public static ProductoMarca ToProductoMarca(Dictionary<string, System.Text.Json.JsonElement> row) => new()
    {
        CodigoProducto = row.GetStringOrDefault("PRODUCTOS[COD_PROD]"),
        NombreProducto = row.GetStringOrDefault("PRODUCTOS[Nombre_Producto]"),
        Marca = row.GetStringOrDefault("PRODUCTOS[Marca]")
    };

    public static VendedorInfo ToVendedorInfo(Dictionary<string, System.Text.Json.JsonElement> row) => new()
    {
        CodigoGrupo = row.GetStringOrDefault("VENDEDORES[COD_GRUPO]"),
        Grupo = row.GetStringOrDefault("VENDEDORES[GRUPO]"),
        CodigoRuta = row.GetStringOrDefault("VENDEDORES[COD_RUTA]"),
        CodigoVendedor = row.GetStringOrDefault("VENDEDORES[COD_VEND]"),
        NombreVendedor = row.GetStringOrDefault("VENDEDORES[NOMVEN]"),
        Subgrupo = row.GetStringOrDefault("VENDEDORES[SUBGRUPO]"),
        SubGrupo2 = row.GetStringOrDefault("VENDEDORES[SUB_GRUPO2]")
    };

    public static ReporteInfo ToReporteInfo(Dictionary<string, System.Text.Json.JsonElement> row) => new()
    {
        Reporte = row.GetStringOrDefault("REPORTES[REPORTE]")
    };
}
