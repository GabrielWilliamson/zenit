namespace Zenit.Models;

public sealed class MonthOption
{
    public int Value { get; set; }

    public string Name { get; set; } = string.Empty;

    public override string ToString() => Name;
}
