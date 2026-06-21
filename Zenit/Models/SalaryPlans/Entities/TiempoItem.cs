namespace Zenit.Models.SalaryPlans.Entities;
public sealed class TiempoItem
{
    public DateTime Fecha { get; init; }
    public int FechaAnio { get; init; }
    public int FechaMes { get; init; }
    public int DiaNumero { get; init; }
    public int Semestre { get; init; }
    public string MesNombre { get; init; } = string.Empty;
    public string DiaNombre { get; init; } = string.Empty;
    public string MesYAnio { get; init; } = string.Empty;
    public string Orden { get; init; } = string.Empty;
}
