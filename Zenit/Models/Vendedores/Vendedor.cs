using CommunityToolkit.Mvvm.ComponentModel;
using Zenit.Helpers;

namespace Zenit.Models.Vendedores;

/// <summary>
/// Modelo de mantenimiento de vendedores.
/// Coincide con la tabla existente Vendedores.
/// </summary>
public sealed class Vendedor : ObservableObject
{
    private string _codVend = string.Empty;
    private string _codGrupo = string.Empty;
    private string _grupo = string.Empty;
    private string _codRuta = string.Empty;
    private string _nomven = string.Empty;
    private string _subgrupo = string.Empty;
    private string _subGrupo2 = string.Empty;
    private string _telefono = string.Empty;

    public string COD_VEND
    {
        get => _codVend;
        set => SetProperty(ref _codVend, value);
    }

    public string COD_GRUPO
    {
        get => _codGrupo;
        set => SetProperty(ref _codGrupo, value);
    }

    public string GRUPO
    {
        get => _grupo;
        set => SetProperty(ref _grupo, value);
    }

    public string COD_RUTA
    {
        get => _codRuta;
        set => SetProperty(ref _codRuta, value);
    }

    public string NOMVEN
    {
        get => _nomven;
        set => SetProperty(ref _nomven, value);
    }

    public string SUBGRUPO
    {
        get => _subgrupo;
        set => SetProperty(ref _subgrupo, value);
    }

    public string SUB_GRUPO2
    {
        get => _subGrupo2;
        set => SetProperty(ref _subGrupo2, value);
    }

    public string TELEFONO
    {
        get => _telefono;
        set => SetProperty(ref _telefono, VendedorTelefonoHelper.FormatForDisplay(value));
    }

    public override string ToString()
    {
        if (string.IsNullOrWhiteSpace(COD_VEND) && string.IsNullOrWhiteSpace(NOMVEN))
            return "Nuevo vendedor";

        return $"{COD_VEND} - {NOMVEN}".Trim(' ', '-');
    }
}
