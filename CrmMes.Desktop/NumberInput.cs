using System.Globalization;

namespace CrmMes.Desktop;

/// <summary>The one place typed numbers are read. Before this, most screens parsed with the Italian
/// culture — where '.' is the thousands separator, so "1.5" silently became 15 — while the quality
/// screens parsed with the invariant culture, where ',' is the thousands separator, so an operator
/// typing "12,5" recorded 125. Both were silent: no error, just a wrong quantity or measurement.
///
/// Rule, unambiguous for either keyboard habit:
/// a comma present means comma = decimal and dots = thousands ("1.250,50" -> 1250.50);
/// no comma, a single dot is the decimal point ("12.5" -> 12.5);
/// several dots are thousands separators ("1.250.000" -> 1250000).</summary>
public static class NumberInput
{
    public static bool TryParseDecimal(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim().Replace(" ", string.Empty).Replace(" ", string.Empty);
        var commaCount = normalized.Count(c => c == ',');
        var dotCount = normalized.Count(c => c == '.');

        if (commaCount > 1)
        {
            return false;
        }

        if (commaCount == 1)
        {
            normalized = normalized.Replace(".", string.Empty).Replace(',', '.');
        }
        else if (dotCount > 1)
        {
            normalized = normalized.Replace(".", string.Empty);
        }

        return decimal.TryParse(
            normalized,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out value);
    }

    public static decimal? ParseOptionalDecimal(string? text) =>
        TryParseDecimal(text, out var value) ? value : null;
}
