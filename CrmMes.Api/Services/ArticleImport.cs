using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace CrmMes.Api.Services;

/// <summary>Article (material) lists from Excel or CSV, as exported by the ERP or management software a
/// company already uses. The layout is not imposed: the header row is found among the first rows (a title
/// above it is fine) and columns are recognised by their usual names, in any order. Units, prices, VAT and
/// dates are read the Italian way. Every problem is reported with its row number; nothing is guessed.</summary>
public static class ArticleImport
{
    public const int MaxRows = 50_000;

    public sealed record Article(int Row, string Code, string Name, string Unit, decimal? Price, decimal? VatRate, DateTime? CreatedAt, decimal? MinStock);

    public sealed record Issue(int Row, string? Code, string Message);

    public sealed record ParseResult(List<Article> Articles, List<Issue> Errors, List<Issue> Warnings, Dictionary<string, string> Columns);

    // Column → accepted header names, compared without spaces, dots, accents and case.
    private static readonly Dictionary<string, string[]> Synonyms = new()
    {
        ["code"] = ["articolo", "codice", "codicearticolo", "codarticolo", "cod", "code", "item", "sku", "codiceprodotto"],
        ["name"] = ["descrizione", "description", "nome", "denominazione", "descrizionearticolo"],
        ["unit"] = ["umbase", "um", "unitamisura", "unitadimisura", "unita", "unit", "uom", "misura"],
        ["price"] = ["prezzobase", "prezzo", "prezzolistino", "listino", "price", "prezzounitario", "costo"],
        ["vat"] = ["codiva", "iva", "aliquota", "aliquotaiva", "vat", "codiceiva"],
        ["created"] = ["datacreazione", "datainserimento", "creato", "created", "data"],
        ["minStock"] = ["scortaminima", "scortamin", "minimo", "minstock", "sottoscorta"],
    };

    private static readonly Dictionary<string, string> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NR"] = "pz", ["N"] = "pz", ["N."] = "pz", ["PZ"] = "pz", ["PZ."] = "pz", ["PEZZI"] = "pz", ["PEZZO"] = "pz", ["NUMERO"] = "pz",
        ["METRO"] = "m", ["METRI"] = "m", ["MT"] = "m", ["M"] = "m", ["ML"] = "m",
        ["CONFEZ"] = "conf", ["CONF"] = "conf", ["CONFEZIONE"] = "conf", ["CF"] = "conf",
        ["MATASSA"] = "matassa", ["BOBINA"] = "bobina", ["ROTOLO"] = "rotolo", ["SCATOLA"] = "scatola",
        ["CHILOMET"] = "km", ["KM"] = "km", ["KG"] = "kg", ["CHILO"] = "kg", ["G"] = "g", ["GR"] = "g",
        ["L"] = "l", ["LT"] = "l", ["LITRO"] = "l", ["LITRI"] = "l", ["MQ"] = "m²", ["MC"] = "m³", ["H"] = "h", ["ORE"] = "h",
    };

    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

    public static string NormalizeUnit(string? unit)
    {
        var trimmed = (unit ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return "pz";
        }

        return Units.TryGetValue(trimmed, out var known) ? known : trimmed.ToLowerInvariant()[..Math.Min(trimmed.Length, 20)];
    }

    /// <summary>The worksheet as text, row by row (numbers in invariant form, dates as dd/MM/yyyy).</summary>
    public static List<string?[]> ReadExcel(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        var used = sheet.RangeUsed();
        if (used is null)
        {
            return [];
        }

        var lastColumn = used.LastColumn().ColumnNumber();
        var rows = new List<string?[]>();
        foreach (var row in sheet.Rows(1, Math.Min(used.LastRow().RowNumber(), MaxRows + 20)))
        {
            var cells = new string?[lastColumn];
            for (var c = 1; c <= lastColumn; c++)
            {
                var value = row.Cell(c).Value;
                cells[c - 1] = value.Type switch
                {
                    XLDataType.Blank => null,
                    XLDataType.Number => value.GetNumber().ToString(CultureInfo.InvariantCulture),
                    XLDataType.DateTime => value.GetDateTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                    XLDataType.Boolean => value.GetBoolean() ? "1" : "0",
                    _ => value.ToString(CultureInfo.InvariantCulture),
                };
            }

            rows.Add(cells);
        }

        return rows;
    }

    /// <summary>CSV with ';' (Italian Excel) or ',' separators, quoted fields allowed.</summary>
    public static List<string?[]> ReadCsv(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var separator = lines.FirstOrDefault(l => l.Trim().Length > 0)?.Count(ch => ch == ';') >= 1 ? ';' : ',';
        return lines.Take(MaxRows + 20).Select(line => SplitCsv(line, separator)).ToList();
    }

    private static string?[] SplitCsv(string line, char separator)
    {
        var cells = new List<string?>();
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
            else if (ch == separator) { cells.Add(current.ToString()); current.Clear(); }
            else { current.Append(ch); }
        }

        cells.Add(current.ToString());
        return [.. cells];
    }

    public static ParseResult Parse(IReadOnlyList<string?[]> rows)
    {
        var errors = new List<Issue>();
        var warnings = new List<Issue>();
        var articles = new List<Article>();

        var headerIndex = -1;
        Dictionary<string, int> columns = [];
        for (var r = 0; r < Math.Min(rows.Count, 15) && headerIndex < 0; r++)
        {
            var found = MapColumns(rows[r]);
            if (found.ContainsKey("code") && found.ContainsKey("name"))
            {
                headerIndex = r;
                columns = found;
            }
        }

        if (headerIndex < 0)
        {
            errors.Add(new Issue(0, null, "Intestazioni non trovate: servono almeno una colonna del codice (es. \"Articolo\" o \"Codice\") e una della descrizione (\"Descrizione\")."));
            return new ParseResult(articles, errors, warnings, []);
        }

        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var r = headerIndex + 1; r < rows.Count; r++)
        {
            var rowNumber = r + 1; // as Excel shows it
            string? Cell(string key) => columns.TryGetValue(key, out var c) && c < rows[r].Length ? rows[r][c]?.Trim() : null;

            var code = Cell("code") ?? string.Empty;
            var name = Cell("name") ?? string.Empty;
            if (rows[r].All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (code.Length == 0)
            {
                errors.Add(new Issue(rowNumber, null, name.Length > 0 ? $"Codice mancante per \"{Shorten(name)}\"." : "Riga senza codice."));
                continue;
            }

            if (code.Length > 100)
            {
                errors.Add(new Issue(rowNumber, Shorten(code), "Codice più lungo di 100 caratteri."));
                continue;
            }

            if (seen.TryGetValue(code, out var firstRow))
            {
                errors.Add(new Issue(rowNumber, code, $"Codice ripetuto: compare già alla riga {firstRow}."));
                continue;
            }

            seen[code] = rowNumber;
            if (name.Length == 0)
            {
                warnings.Add(new Issue(rowNumber, code, "Descrizione mancante: uso il codice come descrizione."));
                name = code;
            }

            name = name.Replace("\r", " ").Replace("\n", " ").Replace("  ", " ");
            if (name.Length > 250)
            {
                warnings.Add(new Issue(rowNumber, code, "Descrizione accorciata a 250 caratteri."));
                name = name[..250];
            }

            var price = ParseDecimal(Cell("price"), rowNumber, code, "prezzo", warnings);
            if (price < 0)
            {
                warnings.Add(new Issue(rowNumber, code, "Prezzo negativo ignorato."));
                price = null;
            }

            var vat = ParseVat(Cell("vat"), rowNumber, code, warnings);
            var minStock = ParseDecimal(Cell("minStock"), rowNumber, code, "scorta minima", warnings);
            var created = ParseDate(Cell("created"));
            articles.Add(new Article(rowNumber, code, name, NormalizeUnit(Cell("unit")), price, vat, created, minStock is >= 0 ? minStock : null));
            if (articles.Count >= MaxRows)
            {
                warnings.Add(new Issue(rowNumber, code, $"Limite di {MaxRows} articoli per file: le righe successive sono ignorate."));
                break;
            }
        }

        var names = columns.ToDictionary(pair => pair.Key, pair => rows[headerIndex][pair.Value]?.Trim() ?? string.Empty);
        return new ParseResult(articles, errors, warnings, names);
    }

    private static Dictionary<string, int> MapColumns(string?[] header)
    {
        var result = new Dictionary<string, int>();
        for (var c = 0; c < header.Length; c++)
        {
            var key = Key(header[c]);
            if (key.Length == 0)
            {
                continue;
            }

            foreach (var (column, names) in Synonyms)
            {
                if (!result.ContainsKey(column) && names.Contains(key))
                {
                    result[column] = c;
                }
            }
        }

        return result;
    }

    private static string Key(string? text)
    {
        var normalized = (text ?? string.Empty).Normalize(NormalizationForm.FormD);
        return new string(normalized.Where(ch => char.IsLetterOrDigit(ch) && CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .Select(char.ToLowerInvariant).ToArray());
    }

    private static decimal? ParseDecimal(string? text, int row, string code, string what, List<Issue> warnings)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = text.Replace("€", string.Empty).Replace(" ", string.Empty).Trim();
        // "1.234,50" (Italian) or "1234.5" (from Excel numbers, invariant).
        if (cleaned.Contains(',') && decimal.TryParse(cleaned, NumberStyles.Number, Italian, out var italian))
        {
            return italian;
        }

        if (decimal.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariant))
        {
            return Math.Round(invariant, 6);
        }

        warnings.Add(new Issue(row, code, $"Valore di {what} non numerico (\"{text}\"): ignorato."));
        return null;
    }

    /// <summary>"22", "22%", "10": the rate. Codes that aren't a rate (exempt, N4...) are left to the invoice.</summary>
    private static decimal? ParseVat(string? text, int row, string code, List<Issue> warnings)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = text.Replace("%", string.Empty).Trim();
        if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var rate) ||
            decimal.TryParse(cleaned, NumberStyles.Number, Italian, out rate))
        {
            if (rate is >= 0 and <= 100)
            {
                return rate;
            }
        }

        warnings.Add(new Issue(row, code, $"Codice IVA \"{text}\" non è un'aliquota: lasciato vuoto (si sceglie in fattura)."));
        return null;
    }

    private static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] formats = ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy"];
        return DateTime.TryParseExact(text.Trim().Split(' ')[0], formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date)
            ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
            : null;
    }

    private static string Shorten(string text) => text.Length <= 40 ? text : text[..40] + "...";

    /// <summary>The same columns the import reads, so an exported file can be edited and imported back.</summary>
    public static byte[] Export(IEnumerable<CrmMes.Core.Models.Material> materials)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Articoli");
        string[] headers = ["Articolo", "Descrizione", "CodIVA", "UMBase", "PrezzoBase", "DataCreazione", "Giacenza", "ScortaMinima", "Attivo"];
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }

        var row = 2;
        foreach (var material in materials)
        {
            sheet.Cell(row, 1).Value = material.Code;
            sheet.Cell(row, 2).Value = material.Name;
            if (material.VatRate is { } vat) sheet.Cell(row, 3).Value = vat;
            sheet.Cell(row, 4).Value = material.Unit;
            if (material.ListPrice is { } price) sheet.Cell(row, 5).Value = price;
            sheet.Cell(row, 6).Value = material.CreatedAt.ToLocalTime().Date;
            sheet.Cell(row, 6).Style.DateFormat.Format = "dd/mm/yyyy";
            sheet.Cell(row, 7).Value = material.Stock;
            sheet.Cell(row, 8).Value = material.MinStock;
            sheet.Cell(row, 9).Value = material.IsActive ? "SI" : "NO";
            row++;
        }

        var header = sheet.Range(1, 1, 1, headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2FD");
        sheet.Column(5).Style.NumberFormat.Format = "#,##0.00";
        sheet.SheetView.FreezeRows(1);
        sheet.Columns(1, headers.Length).AdjustToContents(1, Math.Min(row, 500));
        sheet.Column(2).Width = Math.Min(sheet.Column(2).Width, 70);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
