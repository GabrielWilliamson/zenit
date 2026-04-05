using System.Collections.Generic;

namespace Zenit.Core.Infrastructure.PowerBi.Models;

public class PowerBiQueryResult
{
    public Dictionary<string, object?> Values { get; set; } = new();
}
