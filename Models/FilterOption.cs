using Zenit.Infrastructure.PowerBi.Reports;

namespace Zenit.Models;

/// <summary>
/// Opción para ComboBox de filtros.
/// - Value == null => "Todos" (sin filtro)
/// </summary>
public sealed class FilterOption
{
    public required string DisplayName { get; init; }
    public DaxFilterValue? Value { get; init; }

    public override string ToString() => DisplayName;
}
