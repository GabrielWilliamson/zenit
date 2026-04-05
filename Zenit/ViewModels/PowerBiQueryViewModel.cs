using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zenit.Core.Infrastructure.PowerBi.Queries;

namespace Zenit.ViewModels;

public partial class PowerBiQueryViewModel : ObservableRecipient
{
    private readonly ExecuteQueryService _queryService;

    [ObservableProperty]
    private string jsonResponse = string.Empty;

    [ObservableProperty]
    private string datasetId = string.Empty;

    [ObservableProperty]
    private string daxQuery = "EVALUATE ROW(\"Ping\", 1)";

    [ObservableProperty]
    private bool isBusy;

    public PowerBiQueryViewModel(ExecuteQueryService queryService)
    {
        _queryService = queryService;
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
}
