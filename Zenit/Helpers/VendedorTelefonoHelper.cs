namespace Zenit.Helpers;

public static class VendedorTelefonoHelper
{
    private const string CountryPrefix = "505";
    private const int LocalDigitsLength = 8;
    private const int FullDigitsLength = 11;

    public static string? NormalizeForStorage(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var digits = DigitsOnly(input);
        if (digits.Length == 0)
            return null;

        if (digits.Length == LocalDigitsLength)
            return CountryPrefix + digits;

        if (digits.Length == FullDigitsLength && digits.StartsWith(CountryPrefix, StringComparison.Ordinal))
            return digits;

        throw new InvalidOperationException("TELEFONO debe tener 8 digitos locales o 11 incluyendo el prefijo 505.");
    }

    public static string FormatForDisplay(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var digits = DigitsOnly(input);
        if (digits.Length == FullDigitsLength && digits.StartsWith(CountryPrefix, StringComparison.Ordinal))
            digits = digits[CountryPrefix.Length..];

        if (digits.Length == LocalDigitsLength)
            return $"{digits[..4]}-{digits[4..]}";

        return digits;
    }

    private static string DigitsOnly(string input)
    {
        var chars = input.Where(char.IsDigit).ToArray();
        return chars.Length == 0 ? string.Empty : new string(chars);
    }
}
