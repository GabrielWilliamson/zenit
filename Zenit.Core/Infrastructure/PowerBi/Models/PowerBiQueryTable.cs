using System.Collections.Generic;

namespace Zenit.Core.Infrastructure.PowerBi.Models;

public sealed class PowerBiQueryTable
{
    public string Name { get; set; } = string.Empty;

    public List<PowerBiQueryColumn> Columns { get; } = new();

    public List<Dictionary<string, object?>> Rows { get; } = new();
}

public sealed class PowerBiQueryColumn
{
    public string Name { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;
}
