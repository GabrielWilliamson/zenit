using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zenit.Models.SalaryPlans.Entities;
using Zenit.Services.SalaryPlans;

namespace Zenit.ViewModels;

// este es el codigo que tengo actualmente para probar pero debes de actualizarlo
// ademas debes de mostrar los resultados para cada llamada a la api para ver ande bien
// public partial class SalaryPlansGeneratorViewModel : ObservableRecipient
// {
//    private readonly ExecuteQueryService _queryService;

//     [ObservableProperty]
//     private string jsonResponse = string.Empty;

//     [ObservableProperty]
//     private string datasetId = string.Empty;

//     [ObservableProperty]
//     private string daxQuery = "EVALUATE ROW(\"Ping\", 1)";

//     [ObservableProperty]
//     private bool isBusy;

//     public PowerBiQueryViewModel(ExecuteQueryService queryService)
//     {
//         _queryService = queryService;
//     }

//     public async Task RunQueryAsync(string datasetId, string dax)
//     {
//         JsonResponse = await _queryService.ExecuteAsync(datasetId, dax);
//     }

//     [RelayCommand]
//     private async Task ExecuteAsync()
//     {
//         if (string.IsNullOrWhiteSpace(DatasetId) || string.IsNullOrWhiteSpace(DaxQuery))
//             return;

//         try
//         {
//             IsBusy = true;
//             await RunQueryAsync(DatasetId.Trim(), DaxQuery);
//         }
//         finally
//         {
//             IsBusy = false;
//         }
//     }
// }




public partial class SalaryPlansGeneratorViewModel : ObservableObject
{
    private readonly IPowerBiQueryService _queryService;
    private readonly PowerBiDataService _powerBiDataService;

    [ObservableProperty]
    private string datasetId = string.Empty;

    [ObservableProperty]
    private string daxQuery = string.Empty;

    [ObservableProperty]
    private string jsonResponse = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    public ObservableCollection<Medicion> Mediciones { get; } = new();
    public ObservableCollection<MedicionMeta> MetasVentas { get; } = new();
    public ObservableCollection<string> Marcas { get; } = new();
    public ObservableCollection<VendedorInfo> Vendedores { get; } = new();

    public SalaryPlansGeneratorViewModel(IPowerBiQueryService queryService)
    {
        _queryService = queryService;
        _powerBiDataService = new PowerBiDataService(_queryService);
    }

    public async Task RunQueryAsync(string datasetId, string dax)
    {
        JsonResponse = await _queryService.ExecuteAsync(datasetId, dax);
    }

    [RelayCommand]
    private async Task ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(DatasetId) || string.IsNullOrWhiteSpace(DaxQuery))
            return;

        try
        {
            IsBusy = true;
            await RunQueryAsync(DatasetId.Trim(), DaxQuery);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadMedicionesAsync()
    {
        if (string.IsNullOrWhiteSpace(DatasetId))
            return;

        try
        {
            IsBusy = true;
            Mediciones.Clear();

            var items = await _powerBiDataService.GetMedicionesAsync(DatasetId.Trim());

            foreach (var item in items)
                Mediciones.Add(item);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadMetasVentasAsync()
    {
        if (string.IsNullOrWhiteSpace(DatasetId))
            return;

        try
        {
            IsBusy = true;
            MetasVentas.Clear();

            var items = await _powerBiDataService.GetMetasVentasAsync(DatasetId.Trim());

            foreach (var item in items)
                MetasVentas.Add(item);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadMarcasAsync()
    {
        if (string.IsNullOrWhiteSpace(DatasetId))
            return;

        try
        {
            IsBusy = true;
            Marcas.Clear();

            var items = await _powerBiDataService.GetMarcasDistinctAsync(DatasetId.Trim());

            foreach (var item in items)
                Marcas.Add(item);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadVendedoresAsync()
    {
        if (string.IsNullOrWhiteSpace(DatasetId))
            return;

        try
        {
            IsBusy = true;
            Vendedores.Clear();

            var items = await _powerBiDataService.GetVendedoresAsync(DatasetId.Trim());

            foreach (var item in items)
                Vendedores.Add(item);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
