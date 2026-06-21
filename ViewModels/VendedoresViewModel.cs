using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zenit.Helpers;
using Zenit.Infrastructure.Logging;
using Zenit.Models.Vendedores;
using Zenit.Services;

namespace Zenit.ViewModels;

public partial class VendedoresViewModel : ObservableRecipient
{
    private const string AllFilterValue = "Todos";

    private readonly VendedoresDataService _vendedoresDataService;
    private readonly ILogger<VendedoresViewModel> _logger;
    private readonly Dictionary<Vendedor, string> _originalKeys = new();

    public ObservableCollection<Vendedor> Vendedores { get; } = new();
    public ObservableCollection<string> GrupoOptions { get; } = new();
    public ObservableCollection<string> SubgrupoOptions { get; } = new();

    [ObservableProperty] private Vendedor? selectedVendedor;
    [ObservableProperty] private string searchNombre = string.Empty;
    [ObservableProperty] private string searchRuta = string.Empty;
    [ObservableProperty] private string selectedGrupo = AllFilterValue;
    [ObservableProperty] private string selectedSubgrupo = AllFilterValue;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool telefonoColumnAvailable = true;

    public bool CanSave => !IsBusy && SelectedVendedor != null;
    public bool CanDelete => !IsBusy && SelectedVendedor != null;

    public VendedoresViewModel(
        VendedoresDataService vendedoresDataService,
        ILogger<VendedoresViewModel> logger)
    {
        _vendedoresDataService = vendedoresDataService;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        TelefonoColumnAvailable = await _vendedoresDataService.HasTelefonoColumnAsync();
        await LoadGruposOptionsAsync();
        await LoadSubgrupoOptionsAsync();
        await RefreshAsync();
    }

    [RelayCommand]
    private void New()
    {
        var vendedor = new Vendedor();
        Vendedores.Add(vendedor);
        _originalKeys[vendedor] = string.Empty;
        SelectedVendedor = vendedor;
        StatusMessage = "Nuevo vendedor listo para editar.";
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (SelectedVendedor == null)
            return;

        try
        {
            IsBusy = true;
            SelectedVendedor.TELEFONO = VendedorTelefonoHelper.FormatForDisplay(
                VendedorTelefonoHelper.NormalizeForStorage(SelectedVendedor.TELEFONO) ?? string.Empty);

            var originalKey = GetOriginalKey(SelectedVendedor);
            if (string.IsNullOrWhiteSpace(originalKey))
            {
                await _vendedoresDataService.CreateAsync(SelectedVendedor);
                _originalKeys[SelectedVendedor] = SelectedVendedor.COD_VEND.Trim();
                StatusMessage = $"Vendedor '{SelectedVendedor.COD_VEND}' creado.";
            }
            else
            {
                await _vendedoresDataService.UpdateAsync(originalKey, SelectedVendedor);
                _originalKeys[SelectedVendedor] = SelectedVendedor.COD_VEND.Trim();
                StatusMessage = $"Vendedor '{SelectedVendedor.COD_VEND}' actualizado.";
            }

            await LoadGruposOptionsAsync();
            await LoadSubgrupoOptionsAsync();
            await RefreshAsync(SelectedVendedor.COD_VEND);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error guardando vendedor.");
            StatusMessage = $"Error al guardar: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        if (SelectedVendedor == null)
            return;

        try
        {
            IsBusy = true;

            var originalKey = GetOriginalKey(SelectedVendedor);
            if (string.IsNullOrWhiteSpace(originalKey))
            {
                Vendedores.Remove(SelectedVendedor);
                _originalKeys.Remove(SelectedVendedor);
                StatusMessage = "Vendedor nuevo eliminado del listado.";
                return;
            }

            await _vendedoresDataService.DeleteAsync(originalKey);
            StatusMessage = $"Vendedor '{originalKey}' eliminado.";

            await LoadGruposOptionsAsync();
            await LoadSubgrupoOptionsAsync();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error eliminando vendedor.");
            StatusMessage = $"Error al eliminar: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RefreshAsync(null);
    }

    private async Task RefreshAsync(string? preserveSelectedCodVend)
    {
        try
        {
            IsBusy = true;

            var data = await _vendedoresDataService.GetVendedoresAsync(
                SearchNombre,
                SearchRuta,
                NormalizeFilter(SelectedGrupo),
                NormalizeFilter(SelectedSubgrupo));

            Vendedores.Clear();
            _originalKeys.Clear();

            foreach (var vendedor in data)
            {
                Vendedores.Add(vendedor);
                _originalKeys[vendedor] = vendedor.COD_VEND;
            }

            SelectedVendedor = !string.IsNullOrWhiteSpace(preserveSelectedCodVend)
                ? Vendedores.FirstOrDefault(v => string.Equals(v.COD_VEND, preserveSelectedCodVend, StringComparison.OrdinalIgnoreCase))
                : Vendedores.FirstOrDefault();

            var phoneNote = TelefonoColumnAvailable
                ? string.Empty
                : " La columna TELEFONO no existe en BD; agrega: ALTER TABLE Vendedores ADD COLUMN TELEFONO TEXT;";

            StatusMessage = $"{Vendedores.Count} vendedor(es) cargados.{phoneNote}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cargando vendedores.");
            StatusMessage = $"Error al cargar vendedores: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedGrupoChanged(string value)
    {
        _ = LoadSubgrupoOptionsAsync();
    }

    partial void OnSelectedVendedorChanged(Vendedor? value)
    {
        SaveCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadGruposOptionsAsync()
    {
        var items = await _vendedoresDataService.GetGruposAsync();
        var current = SelectedGrupo;

        GrupoOptions.Clear();
        GrupoOptions.Add(AllFilterValue);
        foreach (var item in items)
            GrupoOptions.Add(item);

        SelectedGrupo = GrupoOptions.Contains(current) ? current : AllFilterValue;
    }

    private async Task LoadSubgrupoOptionsAsync()
    {
        var items = await _vendedoresDataService.GetSubgruposAsync(NormalizeFilter(SelectedGrupo));
        var current = SelectedSubgrupo;

        SubgrupoOptions.Clear();
        SubgrupoOptions.Add(AllFilterValue);
        foreach (var item in items)
            SubgrupoOptions.Add(item);

        SelectedSubgrupo = SubgrupoOptions.Contains(current) ? current : AllFilterValue;
    }

    private static string? NormalizeFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return string.Equals(value, AllFilterValue, StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();
    }

    private string GetOriginalKey(Vendedor vendedor)
    {
        return _originalKeys.TryGetValue(vendedor, out var key)
            ? key
            : string.Empty;
    }
}
