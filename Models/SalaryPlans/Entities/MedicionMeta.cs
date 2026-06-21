namespace Zenit.Models.SalaryPlans.Entities;

public sealed class MedicionMeta
{
    public int Codigo { get; init; }
    public string TipoObjetoMeta { get; init; } = string.Empty;
    public int CodigoObjetoMeta { get; init; }

    public DateTime? FechaInicio { get; init; }
    public DateTime? FechaFin { get; init; }

    public decimal MetaCordobas { get; init; }
    public int MetaUnidades { get; init; }
    public decimal MetaCajas { get; init; }
    public decimal MetaLitros { get; init; }
    public decimal MetaLibras { get; init; }
    public int MetaCobertura { get; init; }

    public string NombreReporte { get; init; } = string.Empty;
    public string CodVend { get; init; } = string.Empty;
}
