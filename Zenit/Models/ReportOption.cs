namespace Zenit.Models;

public sealed class ReportOption
{
    public string Key { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public override string ToString() => DisplayName;
}
