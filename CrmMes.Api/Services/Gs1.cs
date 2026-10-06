namespace CrmMes.Api.Services;

/// <summary>GS1 numbering: the mod-10 check digit shared by GTIN and SSCC, and the SSCC layout
/// (extension digit + company prefix + serial reference + check digit = 18 digits).</summary>
public static class Gs1
{
    /// <summary>Check digit of a digit string: from the right, weights 3,1,3,1...; the digit that brings
    /// the weighted sum to a multiple of 10.</summary>
    public static int CheckDigit(string digits)
    {
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Solo cifre.", nameof(digits));
        }

        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var digit = digits[digits.Length - 1 - i] - '0';
            sum += digit * (i % 2 == 0 ? 3 : 1);
        }

        return (10 - sum % 10) % 10;
    }

    public static bool IsValidCompanyPrefix(string? prefix) =>
        prefix is { Length: >= 7 and <= 10 } && prefix.All(char.IsAsciiDigit);

    /// <summary>Largest serial reference that fits beside the given prefix (17 digits minus extension
    /// and prefix).</summary>
    public static long MaxSerial(string prefix) => (long)Math.Pow(10, 16 - prefix.Length) - 1;

    public static string Sscc(string companyPrefix, long serial, int extensionDigit = 0)
    {
        if (!IsValidCompanyPrefix(companyPrefix))
        {
            throw new ArgumentException("Prefisso aziendale GS1 non valido.", nameof(companyPrefix));
        }

        if (serial < 0 || serial > MaxSerial(companyPrefix))
        {
            throw new ArgumentOutOfRangeException(nameof(serial), "Numeratore SSCC esaurito per questo prefisso.");
        }

        var body = $"{extensionDigit}{companyPrefix}{serial.ToString().PadLeft(16 - companyPrefix.Length, '0')}";
        return body + CheckDigit(body);
    }

    public static bool IsValidSscc(string? sscc) =>
        sscc is { Length: 18 } && sscc.All(char.IsAsciiDigit) && CheckDigit(sscc[..17]) == sscc[17] - '0';

    /// <summary>GTIN-8, GTIN-12, GTIN-13 or GTIN-14: the last digit is the check digit of the others.</summary>
    public static bool IsValidGtin(string? gtin) =>
        gtin is { Length: 8 or 12 or 13 or 14 } && gtin.All(char.IsAsciiDigit)
        && CheckDigit(gtin[..^1]) == gtin[^1] - '0';
}
