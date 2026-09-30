using System.Globalization;
using System.Text;

namespace CrmMes.Desktop;

/// <summary>GS1-128 barcodes for pallet labels (SSCC) and product labels (GTIN, lot, best-before).
/// Code 128 is drawn from its published module table; GS1 data starts with FNC1 and fixed-length
/// numeric application identifiers are encoded in code set C (two digits per symbol). A variable-length
/// field such as the lot (AI 10) needs code set B and a trailing FNC1 separator, both handled here.</summary>
public static class Gs1Barcode
{
    /// <summary>Bar/space widths (in modules) of Code 128 symbol values 0–106; 106 is the stop pattern.</summary>
    public static readonly string[] Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
    ];

    private const int StartB = 104;
    private const int StartC = 105;
    private const int CodeB = 100;
    private const int CodeC = 99;
    private const int Fnc1 = 102;
    private const int Stop = 106;

    public static int CheckDigit(string digits)
    {
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Solo cifre.", nameof(digits));
        }

        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            sum += (digits[digits.Length - 1 - i] - '0') * (i % 2 == 0 ? 3 : 1);
        }

        return (10 - sum % 10) % 10;
    }

    public static bool IsValidSscc(string? sscc) =>
        sscc is { Length: 18 } && sscc.All(char.IsAsciiDigit) && CheckDigit(sscc[..17]) == sscc[17] - '0';

    /// <summary>One GS1 element: application identifier and value. Variable-length values need a
    /// separator when another element follows.</summary>
    public sealed record Element(string Ai, string Value, bool VariableLength = false);

    /// <summary>Symbol values of the whole barcode, start and check symbol included, stop excluded.</summary>
    public static List<int> Encode(IReadOnlyList<Element> elements)
    {
        if (elements.Count == 0)
        {
            throw new ArgumentException("Nessun dato da codificare.", nameof(elements));
        }

        var values = new List<int>();
        var set = 'C';
        values.Add(StartC);
        values.Add(Fnc1);
        for (var index = 0; index < elements.Count; index++)
        {
            var element = elements[index];
            var data = element.Ai + element.Value;
            var position = 0;
            while (position < data.Length)
            {
                var digitsAhead = 0;
                while (position + digitsAhead < data.Length && char.IsAsciiDigit(data[position + digitsAhead]))
                {
                    digitsAhead++;
                }

                if (digitsAhead >= 2)
                {
                    if (set != 'C')
                    {
                        values.Add(CodeC);
                        set = 'C';
                    }

                    var pairs = digitsAhead / 2;
                    for (var p = 0; p < pairs; p++)
                    {
                        values.Add(int.Parse(data.AsSpan(position, 2), CultureInfo.InvariantCulture));
                        position += 2;
                    }

                    continue;
                }

                if (set != 'B')
                {
                    values.Add(CodeB);
                    set = 'B';
                }

                var character = data[position];
                if (character < 32 || character > 126)
                {
                    throw new ArgumentException($"Carattere non codificabile: '{character}'.", nameof(elements));
                }

                values.Add(character - 32);
                position++;
            }

            if (element.VariableLength && index < elements.Count - 1)
            {
                values.Add(Fnc1); // separator after a variable-length field
            }
        }

        var checksum = values[0];
        for (var i = 1; i < values.Count; i++)
        {
            checksum += values[i] * i;
        }

        values.Add(checksum % 103);
        return values;
    }

    /// <summary>Bar and space widths in modules, quiet zones excluded (bars at even indexes).</summary>
    public static List<int> Modules(IReadOnlyList<Element> elements)
    {
        var widths = new List<int>();
        foreach (var value in Encode(elements).Append(Stop))
        {
            widths.AddRange(Patterns[value].Select(c => c - '0'));
        }

        return widths;
    }

    /// <summary>The barcode as SVG, in module units, with 10-module quiet zones on both sides.</summary>
    public static string Svg(IReadOnlyList<Element> elements, int heightModules = 50)
    {
        var widths = Modules(elements);
        var total = widths.Sum() + 20;
        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {total} {heightModules}\" shape-rendering=\"crispEdges\">");
        svg.Append(CultureInfo.InvariantCulture, $"<rect width=\"{total}\" height=\"{heightModules}\" fill=\"#fff\"/>");
        var x = 10;
        for (var i = 0; i < widths.Count; i++)
        {
            if (i % 2 == 0)
            {
                svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{x}\" y=\"0\" width=\"{widths[i]}\" height=\"{heightModules}\" fill=\"#000\"/>");
            }

            x += widths[i];
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    /// <summary>Human-readable form under the bars: "(00) 080123450000000425".</summary>
    public static string HumanReadable(IReadOnlyList<Element> elements) =>
        string.Join(" ", elements.Select(e => $"({e.Ai}) {e.Value}"));

    public static Element SsccElement(string sscc) =>
        IsValidSscc(sscc) ? new Element("00", sscc) : throw new ArgumentException("SSCC non valido.", nameof(sscc));

    public static Element BestBeforeElement(DateTime date) => new("15", date.ToString("yyMMdd", CultureInfo.InvariantCulture));

    public static Element UseByElement(DateTime date) => new("17", date.ToString("yyMMdd", CultureInfo.InvariantCulture));

    public static Element CountElement(decimal quantity) =>
        new("37", Math.Round(quantity).ToString(CultureInfo.InvariantCulture), VariableLength: true);

    public static Element LotElement(string lot)
    {
        var clean = lot.Trim();
        return clean.Length is > 0 and <= 20 ? new Element("10", clean, VariableLength: true) : throw new ArgumentException("Lotto GS1: da 1 a 20 caratteri.", nameof(lot));
    }
}
