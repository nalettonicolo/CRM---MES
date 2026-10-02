using System.Globalization;
using System.Text;

namespace CrmMes.Api.Services;

/// <summary>ANIE/METEL electrical price lists, as manufacturers and wholesalers export them. Two layouts
/// are accepted, because both exist in the wild:
/// <list type="bullet">
/// <item>Fixed-width article records starting with A (brand 3, code 16, description 40, unit 3, list price
/// 11 characters with two implied decimals), with optional T header lines skipped.</item>
/// <item>Delimited text (; | or tab), with or without a header row. Column names are the usual Italian ones
/// (marca, codice, descrizione, UM, prezzo, EAN).</item>
/// </list>
/// A real file is not required to ship the importer: the tests use both layouts. A producer file can still
/// be used later to tune an odd variant, without blocking the module.</summary>
public static class MetelList
{
    public const int MaxRows = 50_000;

    public sealed record Line(int SourceLine, string Brand, string Code, string Name, string Unit, decimal? ListPrice, string? Ean);

    public sealed record ParseResult(List<Line> Lines, List<string> Errors);

    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

    private static readonly Dictionary<string, string[]> Headers = new()
    {
        ["brand"] = ["marca", "sigla", "siglaproduttore", "brand", "produttore", "fornitore"],
        ["code"] = ["codice", "codicearticolo", "codarticolo", "articolo", "code", "partnumber", "riferimento"],
        ["name"] = ["descrizione", "description", "nome", "denominazione"],
        ["unit"] = ["um", "unita", "unitamisura", "unitadimisura", "uom", "unit"],
        ["price"] = ["prezzo", "prezzolistino", "listino", "prezzobase", "price", "prezzounitario"],
        ["ean"] = ["ean", "ean13", "barcode", "codicebarre", "gtin"],
    };

    public static ParseResult Parse(byte[] bytes)
    {
        var text = Decode(bytes);
        var raw = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var lines = new List<string>();
        var numbers = new List<int>();
        for (var i = 0; i < raw.Length && lines.Count < MaxRows + 20; i++)
        {
            var line = raw[i].TrimEnd();
            if (line.Length == 0 || line[0] is '#' or '*')
            {
                continue;
            }

            lines.Add(line);
            numbers.Add(i + 1);
        }

        if (lines.Count == 0)
        {
            return new ParseResult([], ["Il file è vuoto."]);
        }

        var articleLike = lines.Count(l => l.Length >= 63 && (l[0] is 'A' or 'a') && CountSeparators(l) < 2);
        if (articleLike >= Math.Max(1, lines.Count / 3))
        {
            return ParseFixedWidth(lines, numbers);
        }

        return ParseDelimited(lines, numbers);
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        var utf8 = Encoding.UTF8.GetString(bytes);
        return utf8.Contains('\uFFFD') ? Encoding.Latin1.GetString(bytes) : utf8;
    }

    private static int CountSeparators(string line) =>
        line.Count(ch => ch is ';' or '|' or '\t');

    private static ParseResult ParseFixedWidth(List<string> lines, List<int> numbers)
    {
        var articles = new List<Line>();
        var errors = new List<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var type = char.ToUpperInvariant(line[0]);
            if (type is 'T' or 'C' or 'F')
            {
                continue;
            }

            if (type != 'A')
            {
                errors.Add($"Riga {numbers[i]}: tipo record '{line[0]}' non riconosciuto (attesi A, T).");
                continue;
            }

            if (line.Length < 63)
            {
                errors.Add($"Riga {numbers[i]}: record articolo troppo corto ({line.Length} caratteri).");
                continue;
            }

            var brand = Slice(line, 1, 3);
            var code = Slice(line, 4, 16);
            var name = Slice(line, 20, 40);
            var unit = Slice(line, 60, 3);
            var price = line.Length >= 74 ? ParsePrice(Slice(line, 63, 11), impliedDecimals: true) : null;
            var ean = line.Length >= 87 ? Slice(line, 74, 13) : null;
            if (code.Length == 0 || name.Length == 0)
            {
                errors.Add($"Riga {numbers[i]}: codice o descrizione mancanti.");
                continue;
            }

            articles.Add(new Line(numbers[i], brand, code, name, ArticleImport.NormalizeUnit(unit), price, NullIfEmpty(ean)));
        }

        if (articles.Count == 0 && errors.Count == 0)
        {
            errors.Add("Nessun record articolo (tipo A) nel file.");
        }

        return new ParseResult(articles, errors);
    }

    private static ParseResult ParseDelimited(List<string> lines, List<int> numbers)
    {
        var separator = DetectSeparator(lines[0]);
        var first = Split(lines[0], separator);
        var map = MapHeaders(first);
        var start = map is not null ? 1 : 0;
        if (map is null)
        {
            if (first.Length < 3)
            {
                return new ParseResult([], ["Servono almeno marca, codice e descrizione (file delimitato) oppure record fissi tipo A."]);
            }

            map = new Dictionary<string, int> { ["brand"] = 0, ["code"] = 1, ["name"] = 2 };
            if (first.Length > 3) map["unit"] = 3;
            if (first.Length > 4) map["price"] = 4;
            if (first.Length > 5) map["ean"] = 5;
        }

        if (!map.ContainsKey("code") || !map.ContainsKey("name"))
        {
            return new ParseResult([], ["Colonne obbligatorie nel listino Metel: codice e descrizione."]);
        }

        var articles = new List<Line>();
        var errors = new List<string>();
        for (var i = start; i < lines.Count; i++)
        {
            var cells = Split(lines[i], separator);
            var code = Cell(cells, map, "code");
            var name = Cell(cells, map, "name");
            if (code.Length == 0 && name.Length == 0)
            {
                continue;
            }

            if (code.Length == 0 || name.Length == 0)
            {
                errors.Add($"Riga {numbers[i]}: codice o descrizione mancanti.");
                continue;
            }

            articles.Add(new Line(
                numbers[i],
                Cell(cells, map, "brand"),
                code,
                name,
                ArticleImport.NormalizeUnit(Cell(cells, map, "unit")),
                ParsePrice(Cell(cells, map, "price"), impliedDecimals: false),
                NullIfEmpty(Cell(cells, map, "ean"))));
        }

        if (articles.Count == 0)
        {
            errors.Add("Nessun articolo nel file.");
        }

        return new ParseResult(articles, errors);
    }

    private static char DetectSeparator(string line)
    {
        var counts = new Dictionary<char, int>
        {
            [';'] = line.Count(ch => ch == ';'),
            ['|'] = line.Count(ch => ch == '|'),
            ['\t'] = line.Count(ch => ch == '\t'),
        };
        var best = counts.MaxBy(pair => pair.Value);
        return best.Value > 0 ? best.Key : ';';
    }

    private static Dictionary<string, int>? MapHeaders(string[] cells)
    {
        var map = new Dictionary<string, int>();
        for (var i = 0; i < cells.Length; i++)
        {
            var key = NormalizeHeader(cells[i]);
            foreach (var (field, names) in Headers)
            {
                if (names.Contains(key) && !map.ContainsKey(field))
                {
                    map[field] = i;
                }
            }
        }

        return map.ContainsKey("code") && map.ContainsKey("name") ? map : null;
    }

    private static string NormalizeHeader(string value)
    {
        var folded = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(folded.Length);
        foreach (var ch in folded)
        {
            if (char.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static string[] Split(string line, char separator)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else if (ch == '"') { quoted = false; }
                else { current.Append(ch); }
            }
            else if (ch == '"') { quoted = true; }
            else if (ch == separator) { cells.Add(current.ToString().Trim()); current.Clear(); }
            else { current.Append(ch); }
        }

        cells.Add(current.ToString().Trim());
        return [.. cells];
    }

    private static string Cell(string[] cells, Dictionary<string, int> map, string field) =>
        map.TryGetValue(field, out var index) && index < cells.Length ? cells[index].Trim() : "";

    private static string Slice(string line, int start, int length)
    {
        if (start >= line.Length)
        {
            return "";
        }

        var take = Math.Min(length, line.Length - start);
        return line.Substring(start, take).Trim();
    }

    private static decimal? ParsePrice(string text, bool impliedDecimals)
    {
        var value = text.Trim().Replace(" ", "");
        if (value.Length == 0)
        {
            return null;
        }

        if (impliedDecimals && value.All(ch => char.IsDigit(ch) || ch == ' '))
        {
            if (decimal.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var packed))
            {
                return packed / 100m;
            }
        }

        if (value.Contains(',') && !value.Contains('.'))
        {
            return decimal.TryParse(value, NumberStyles.Number, Italian, out var italianOnly) ? italianOnly : null;
        }

        if (value.Contains('.') && !value.Contains(','))
        {
            return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariantOnly) ? invariantOnly : null;
        }

        if (decimal.TryParse(value, NumberStyles.Number, Italian, out var italian))
        {
            return italian;
        }

        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariant) ? invariant : null;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
