using System.Globalization;

namespace CrmMes.Api.Services;

/// <summary>Catalogo G13 — pacchetti verticali (import, anteprima e operazioni settore).</summary>
public static class SectorPacks
{
    public const int MaxPreviewSamples = 10;

    public static IReadOnlyList<SectorPackDefinition> All { get; } =
    [
        new("eplan-bom", "Distinta EPLAN", "Importa righe distinta da export EPLAN (codice, quantità, descrizione).", "Elettrico / quadri", true),
        new("wire-list", "Lista cavi", "Elenco collegamenti cavo: origine, destinazione, sezione e colore.", "Elettrico / cablaggio", true),
        new("dm3708", "DM 37/08", "Documentazione e registri per impianti elettrici (DM 37/08).", "Elettrico / impianti", true),
        new("sal", "SAL cantiere", "Stato avanzamento lavori e consuntivi per commesse edili.", "Edilizia", true),
        new("crew-calendar", "Calendario squadre", "Turni e assegnazione squadre operative.", "Servizi / manutenzione", true),
        new("cert-31", "Certificazione DM 31", "Registro certificazioni e collaudi DM 31.", "Elettrico / impianti", true),
        new("nutrition-table", "Tabella nutrizionale", "Valori nutrizionali per 100 g di prodotto alimentare.", "Alimentare", true),
        new("scales", "Bilance", "Integrazione letture da bilance e pesate di produzione.", "Alimentare / pesatura", true),
    ];

    public static SectorPackDefinition? Find(string key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

    public static SectorPackParseResult<EplanBomRow> ParseEplanBom(string text) =>
        ParseSemicolonRows(text, 3, LooksLikeEplanHeader, (row, parts) =>
        {
            var partNumber = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(partNumber))
            {
                return SectorPackRowOutcome<EplanBomRow>.Fail("Codice articolo obbligatorio.");
            }

            if (!TryParseDecimal(parts[1], out var quantity) || quantity <= 0)
            {
                return SectorPackRowOutcome<EplanBomRow>.Fail("Quantità non valida.");
            }

            return SectorPackRowOutcome<EplanBomRow>.Ok(new EplanBomRow(row, partNumber, quantity, parts[2].Trim()));
        });

    public static SectorPackParseResult<WireListRow> ParseWireList(string text) =>
        ParseSemicolonRows(text, 4, LooksLikeWireListHeader, (row, parts) =>
        {
            var from = parts[0].Trim();
            var to = parts[1].Trim();
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            {
                return SectorPackRowOutcome<WireListRow>.Fail("Origine e destinazione sono obbligatorie.");
            }

            return SectorPackRowOutcome<WireListRow>.Ok(new WireListRow(row, from, to, parts[2].Trim(), parts[3].Trim()));
        });

    public static SectorPackParseResult<NutritionTableRow> ParseNutritionTable(string text) =>
        ParseSemicolonRows(text, 2, LooksLikeNutritionHeader, (row, parts) =>
        {
            var nutrient = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(nutrient))
            {
                return SectorPackRowOutcome<NutritionTableRow>.Fail("Nome nutriente obbligatorio.");
            }

            if (!TryParseDecimal(parts[1], out var per100g))
            {
                return SectorPackRowOutcome<NutritionTableRow>.Fail("Valore per 100 g non valido.");
            }

            return SectorPackRowOutcome<NutritionTableRow>.Ok(new NutritionTableRow(row, nutrient, per100g));
        });

    public static SectorPackParseResult<Dm3708Row> ParseDm3708(string text) =>
        ParseSemicolonRows(text, 5, LooksLikeDm3708Header, (row, parts) =>
        {
            var plantCode = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(plantCode))
            {
                return SectorPackRowOutcome<Dm3708Row>.Fail("Codice impianto obbligatorio.");
            }

            if (!TryParseDecimal(parts[3], out var powerKw) || powerKw < 0)
            {
                return SectorPackRowOutcome<Dm3708Row>.Fail("Potenza (kW) non valida.");
            }

            return SectorPackRowOutcome<Dm3708Row>.Ok(new Dm3708Row(
                row,
                plantCode,
                parts[1].Trim(),
                parts[2].Trim(),
                powerKw,
                parts[4].Trim()));
        });

    public static SectorPackParseResult<SalRow> ParseSal(string text) =>
        ParseSemicolonRows(text, 4, LooksLikeSalHeader, (row, parts) =>
        {
            var workOrderCode = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(workOrderCode))
            {
                return SectorPackRowOutcome<SalRow>.Fail("Codice commessa obbligatorio.");
            }

            if (!TryParseDecimal(parts[1], out var percent) || percent is < 0 or > 100)
            {
                return SectorPackRowOutcome<SalRow>.Fail("Percentuale avanzamento non valida (0–100).");
            }

            if (!TryParseDecimal(parts[2], out var amount) || amount < 0)
            {
                return SectorPackRowOutcome<SalRow>.Fail("Importo SAL non valido.");
            }

            return SectorPackRowOutcome<SalRow>.Ok(new SalRow(row, workOrderCode, percent, amount, parts[3].Trim()));
        });

    public static SectorPackParseResult<CrewCalendarRow> ParseCrewCalendar(string text) =>
        ParseSemicolonRows(text, 4, LooksLikeCrewCalendarHeader, (row, parts) =>
        {
            if (!TryParseDate(parts[0], out var date))
            {
                return SectorPackRowOutcome<CrewCalendarRow>.Fail("Data non valida.");
            }

            var crewName = parts[1].Trim();
            var siteOrOrder = parts[2].Trim();
            if (string.IsNullOrWhiteSpace(crewName) || string.IsNullOrWhiteSpace(siteOrOrder))
            {
                return SectorPackRowOutcome<CrewCalendarRow>.Fail("Squadra e cantiere/commessa sono obbligatori.");
            }

            if (!TryParseDecimal(parts[3], out var hours) || hours <= 0)
            {
                return SectorPackRowOutcome<CrewCalendarRow>.Fail("Ore non valide.");
            }

            return SectorPackRowOutcome<CrewCalendarRow>.Ok(new CrewCalendarRow(row, date, crewName, siteOrOrder, hours));
        });

    public static SectorPackParseResult<Cert31Row> ParseCert31(string text) =>
        ParseSemicolonRows(text, 5, LooksLikeCert31Header, (row, parts) =>
        {
            var lotNumber = parts[0].Trim();
            var materialCode = parts[1].Trim();
            var certificateNumber = parts[2].Trim();
            if (string.IsNullOrWhiteSpace(lotNumber) || string.IsNullOrWhiteSpace(materialCode)
                || string.IsNullOrWhiteSpace(certificateNumber))
            {
                return SectorPackRowOutcome<Cert31Row>.Fail("Lotto, materiale e numero certificato sono obbligatori.");
            }

            if (!TryParseDate(parts[4], out var issuedOn))
            {
                return SectorPackRowOutcome<Cert31Row>.Fail("Data emissione non valida.");
            }

            return SectorPackRowOutcome<Cert31Row>.Ok(new Cert31Row(
                row,
                lotNumber,
                materialCode,
                certificateNumber,
                parts[3].Trim(),
                issuedOn));
        });

    public static SectorPackParseResult<ScaleReadingRow> ParseScaleReadings(string text) =>
        ParseSemicolonRows(text, 5, LooksLikeScalesHeader, (row, parts) =>
        {
            var materialCode = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(materialCode))
            {
                return SectorPackRowOutcome<ScaleReadingRow>.Fail("Codice materiale obbligatorio.");
            }

            if (!TryParseDecimal(parts[1], out var weightKg) || weightKg <= 0)
            {
                return SectorPackRowOutcome<ScaleReadingRow>.Fail("Peso non valido.");
            }

            var unit = string.IsNullOrWhiteSpace(parts[2]) ? "kg" : parts[2].Trim();
            if (!TryParseDateTime(parts[3], out var recordedAt))
            {
                return SectorPackRowOutcome<ScaleReadingRow>.Fail("Timestamp ISO non valido.");
            }

            return SectorPackRowOutcome<ScaleReadingRow>.Ok(new ScaleReadingRow(
                row,
                materialCode,
                weightKg,
                unit,
                recordedAt,
                parts[4].Trim()));
        });

    public static string BuildDm3708Declaration(Dm3708DeclarationInfo info)
    {
        var issued = DateTime.UtcNow.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("it-IT"));
        return $"""
            DICHIARAZIONE DI CONFORMITÀ — DM 37/08
            (D.Lgs. 81/2008 e norme tecniche di riferimento per impianti elettrici)

            Impianto / codice: {info.PlantCode}
            Indirizzo installazione: {info.Address}
            Committente: {info.ClientName}
            Potenza nominale: {info.PowerKw.ToString("0.##", CultureInfo.GetCultureInfo("it-IT"))} kW

            Note:
            {(string.IsNullOrWhiteSpace(info.Notes) ? "—" : info.Notes)}

            Il sottoscritto dichiara che l'impianto elettrico sopra indicato è stato realizzato
            in conformità alle prescrizioni del DM 37/08 e alle norme tecniche applicabili,
            ed è idoneo al funzionamento in sicurezza.

            Data: {issued}

            _______________________________
            Timbro e firma del responsabile tecnico
            """;
    }

    private static bool LooksLikeDm3708Header(string[] parts) =>
        parts.Length >= 1 && parts[0].Contains("PlantCode", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeSalHeader(string[] parts) =>
        parts.Length >= 1 && parts[0].Contains("WorkOrderCode", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeCrewCalendarHeader(string[] parts) =>
        parts.Length >= 1 && parts[0].Contains("Date", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeCert31Header(string[] parts) =>
        parts.Length >= 1 && parts[0].Contains("LotNumber", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeScalesHeader(string[] parts) =>
        parts.Length >= 1 && parts[0].Contains("MaterialCode", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeEplanHeader(string[] parts) =>
        parts.Length >= 1 && parts[0].Contains("PartNumber", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeWireListHeader(string[] parts) =>
        parts.Length >= 2 && parts[0].Contains("From", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeNutritionHeader(string[] parts) =>
        parts.Length >= 1 && parts[0].Contains("Nutrient", StringComparison.OrdinalIgnoreCase);

    private static SectorPackParseResult<TRow> ParseSemicolonRows<TRow>(
        string text,
        int expectedColumns,
        Func<string[], bool> isHeader,
        Func<int, string[], SectorPackRowOutcome<TRow>> map)
    {
        var rows = new List<TRow>();
        var errors = new List<SectorPackLineIssue>();
        var lineNumber = 0;

        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split(';');
            if (parts.Length < expectedColumns)
            {
                errors.Add(new SectorPackLineIssue(lineNumber, $"Servono almeno {expectedColumns} colonne separate da ';'."));
                continue;
            }

            if (lineNumber == 1 && isHeader(parts))
            {
                continue;
            }

            var outcome = map(lineNumber, parts);
            if (outcome.ErrorMessage is { } message)
            {
                errors.Add(new SectorPackLineIssue(lineNumber, message));
                continue;
            }

            rows.Add(outcome.Row!);
        }

        return new SectorPackParseResult<TRow>(rows, errors);
    }

    private static bool TryParseDecimal(string value, out decimal result) =>
        decimal.TryParse(value.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out result)
        || decimal.TryParse(value.Trim(), NumberStyles.Number, CultureInfo.GetCultureInfo("it-IT"), out result);

    private static bool TryParseDate(string value, out DateTime result)
    {
        var trimmed = value.Trim();
        if (DateTime.TryParse(trimmed, CultureInfo.GetCultureInfo("it-IT"), DateTimeStyles.AssumeUniversal, out result)
            || DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result)
            || DateTime.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result))
        {
            result = DateTime.SpecifyKind(result.Date, DateTimeKind.Utc);
            return true;
        }

        result = default;
        return false;
    }

    private static bool TryParseDateTime(string value, out DateTime result)
    {
        var trimmed = value.Trim();
        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result)
            || DateTime.TryParse(trimmed, CultureInfo.GetCultureInfo("it-IT"), DateTimeStyles.AssumeUniversal, out result))
        {
            if (result.Kind == DateTimeKind.Unspecified)
            {
                result = DateTime.SpecifyKind(result, DateTimeKind.Utc);
            }

            return true;
        }

        result = default;
        return false;
    }

    private readonly struct SectorPackRowOutcome<TRow>
    {
        public TRow? Row { get; init; }
        public string? ErrorMessage { get; init; }

        public static SectorPackRowOutcome<TRow> Ok(TRow row) => new() { Row = row };
        public static SectorPackRowOutcome<TRow> Fail(string message) => new() { ErrorMessage = message };
    }
}

public sealed record SectorPackDefinition(string Key, string Name, string Description, string Sector, bool Available);

public sealed record EplanBomRow(int Row, string PartNumber, decimal Quantity, string Description);

public sealed record WireListRow(int Row, string From, string To, string Section, string Color);

public sealed record NutritionTableRow(int Row, string Nutrient, decimal Per100g);

public sealed record Dm3708Row(int Row, string PlantCode, string Address, string ClientName, decimal PowerKw, string Notes);

public sealed record SalRow(int Row, string WorkOrderCode, decimal PercentComplete, decimal Amount, string Notes);

public sealed record CrewCalendarRow(int Row, DateTime Date, string CrewName, string SiteOrWorkOrder, decimal Hours);

public sealed record Cert31Row(int Row, string LotNumber, string MaterialCode, string CertificateNumber, string Issuer, DateTime IssuedOn);

public sealed record ScaleReadingRow(int Row, string MaterialCode, decimal WeightKg, string Unit, DateTime TimestampIso, string Notes);

public sealed record Dm3708DeclarationInfo(string PlantCode, string Address, string ClientName, decimal PowerKw, string? Notes);

public sealed record SectorPackLineIssue(int Row, string Message);

public sealed record SectorPackParseResult<TRow>(IReadOnlyList<TRow> Rows, IReadOnlyList<SectorPackLineIssue> Errors);
