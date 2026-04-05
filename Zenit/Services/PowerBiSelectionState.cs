using Zenit.Core.Infrastructure.PowerBi.Models;

namespace Zenit.Services;

public class PowerBiSelectionState
{
    public PowerBiWorkspace? SelectedWorkspace
    {
        get; set;
    }
    public PowerBiDataset? SelectedDataset
    {
        get; set;
    }
}
