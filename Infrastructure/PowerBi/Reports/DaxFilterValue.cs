using System;
using System.Globalization;

namespace Zenit.Infrastructure.PowerBi.Reports;

/// <summary>
/// Representa un valor de filtro listo para usarse en DAX.
///
/// Ejemplos de DAX literal:
/// - Número: 29
/// - Texto : "018"   (con comillas y escapes DAX si aplica)
///
/// Esta clase existe porque:
/// - En algunos modelos COD_VEND es número (18, 21...)
/// - En otros modelos COD_VEND es texto con ceros a la izquierda ("018", "021"...)
/// - Si "inventamos" el tipo, Power BI puede lanzar:
///   "Text vs Integer"
///
/// Solución lógica:
/// 👉 Traer los valores desde el dataset (ExecuteQueries) y guardar el tipo real.
/// </summary>
public sealed record DaxFilterValue
{
    public object Raw { get; }
    public string Display { get; }
    public string DaxLiteral { get; }

    private DaxFilterValue(object raw, string display, string daxLiteral)
    {
        Raw = raw;
        Display = display;
        DaxLiteral = daxLiteral;
    }

    public static DaxFilterValue FromRaw(object raw)
    {
        if (raw is null) throw new ArgumentNullException(nameof(raw));

        // Números enteros (lo más común en COD_VEND numérico)
        if (raw is int i) return FromNumber(i);
        if (raw is long l) return FromNumber(l);
        if (raw is short s) return FromNumber(s);
        if (raw is byte b) return FromNumber(b);

        // Decimales / double
        if (raw is double d) return FromDouble(d);
        if (raw is float f) return FromDouble(f);
        if (raw is decimal m) return FromDecimal(m);

        // Texto (COD_VEND tipo texto, "018", etc.)
        var str = raw.ToString() ?? string.Empty;
        return FromString(str);
    }

    private static DaxFilterValue FromNumber(long value)
    {
        var display = value.ToString(CultureInfo.InvariantCulture);
        var literal = display; // sin comillas
        return new DaxFilterValue(value, display, literal);
    }

    private static DaxFilterValue FromDouble(double value)
    {
        // Si viene 18.0, lo tratamos como entero 18
        if (Math.Abs(value - Math.Round(value)) < 0.0000001)
        {
            var asLong = Convert.ToInt64(Math.Round(value));
            return FromNumber(asLong);
        }

        var display = value.ToString(CultureInfo.InvariantCulture);
        var literal = display;
        return new DaxFilterValue(value, display, literal);
    }

    private static DaxFilterValue FromDecimal(decimal value)
    {
        // Similar a double: si es entero, lo dejamos como entero
        if (decimal.Truncate(value) == value)
        {
            var asLong = (long)value;
            return FromNumber(asLong);
        }

        var display = value.ToString(CultureInfo.InvariantCulture);
        var literal = display;
        return new DaxFilterValue(value, display, literal);
    }

    private static DaxFilterValue FromString(string value)
    {
        var display = value;
        var escaped = EscapeDaxString(value);
        var literal = $"\"{escaped}\""; // con comillas DAX
        return new DaxFilterValue(value, display, literal);
    }

    /// <summary>
    /// Escape DAX para string literal: duplica comillas.
    /// Ej:  He said "Hi"  =>  He said ""Hi""
    /// </summary>
    private static string EscapeDaxString(string value)
        => value.Replace("\"", "\"\"");
}
