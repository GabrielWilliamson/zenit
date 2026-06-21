namespace Zenit.Infrastructure.PowerBi.Models;

public class PowerBiDataset
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Id : Name;
}
