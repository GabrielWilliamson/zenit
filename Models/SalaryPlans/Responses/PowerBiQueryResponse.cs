using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zenit.Models.SalaryPlans.Responses;

public sealed class PowerBiQueryResponse
{
    [JsonPropertyName("results")]
    public List<PowerBiResult> Results { get; set; } = new();
}

public sealed class PowerBiResult
{
    [JsonPropertyName("tables")]
    public List<PowerBiTable> Tables { get; set; } = new();
}

public sealed class PowerBiTable
{
    [JsonPropertyName("rows")]
    public List<Dictionary<string, JsonElement>> Rows { get; set; } = new();
}
