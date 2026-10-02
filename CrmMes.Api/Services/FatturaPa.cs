using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using CrmMes.Core.Models;

namespace CrmMes.Api.Services;

/// <summary>Italian electronic invoice (FatturaPA, "fatture tra privati" FPR12, schema 1.2.3): checks the
/// data the Exchange System requires and builds the XML. Amounts follow the schema's formats (dot as
/// decimal separator, 2 decimals for totals, up to 8 for unit prices and quantities); each line total is
/// rounded half away from zero and the VAT of every rate is computed once on its taxable total, as the
/// SdI checks it.</summary>
public static class FatturaPa
{
    public static readonly XNamespace Ns = "http://ivaservizi.agenziaentrate.gov.it/docs/xsd/fatture/v1.2";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static readonly IReadOnlyDictionary<string, string> PaymentMethods = new Dictionary<string, string>
    {
        ["MP01"] = "Contanti",
        ["MP02"] = "Assegno",
        ["MP05"] = "Bonifico",
        ["MP08"] = "Carta di pagamento",
        ["MP12"] = "RIBA",
        ["MP19"] = "SEPA Direct Debit",
    };

    /// <summary>Nature codes for 0% lines, with the legal reference printed next to them.</summary>
    public static readonly IReadOnlyDictionary<string, string> Natures = new Dictionary<string, string>
    {
        ["N1"] = "Escluse ex art. 15 DPR 633/72",
        ["N2.1"] = "Non soggette ad IVA ai sensi degli artt. da 7 a 7-septies DPR 633/72",
        ["N2.2"] = "Non soggette - altri casi",
        ["N3.1"] = "Non imponibili - esportazioni, art. 8 DPR 633/72",
        ["N3.2"] = "Non imponibili - cessioni intracomunitarie, art. 41 DL 331/93",
        ["N3.3"] = "Non imponibili - cessioni verso San Marino",
        ["N3.4"] = "Non imponibili - operazioni assimilate alle cessioni all'esportazione",
        ["N3.5"] = "Non imponibili - a seguito di dichiarazioni d'intento",
        ["N3.6"] = "Non imponibili - altre operazioni",
        ["N4"] = "Esenti, art. 10 DPR 633/72",
        ["N5"] = "Regime del margine",
        ["N6.1"] = "Inversione contabile - cessione di rottami e altri materiali di recupero",
        ["N6.3"] = "Inversione contabile - subappalto nel settore edile, art. 17 c.6 lett. a) DPR 633/72",
        ["N6.7"] = "Inversione contabile - prestazioni comparto edile e settori connessi, art. 17 c.6 lett. a-ter) DPR 633/72",
        ["N6.9"] = "Inversione contabile - altri casi",
        ["N7"] = "IVA assolta in altro stato UE",
    };

    public static readonly IReadOnlyList<decimal> VatRates = [22m, 10m, 5m, 4m, 0m];

    public sealed record Summary(decimal Rate, string? Nature, decimal Taxable, decimal Tax);

    public static decimal LineTotal(InvoiceLine line) =>
        Round(line.Quantity * line.UnitPrice * (1 - line.DiscountPercent / 100m));

    public static List<Summary> Summaries(IEnumerable<InvoiceLine> lines) => lines
        .GroupBy(line => (line.VatRate, Nature: line.VatRate == 0 ? line.VatNature : null))
        .OrderByDescending(group => group.Key.VatRate).ThenBy(group => group.Key.Nature)
        .Select(group =>
        {
            var taxable = group.Sum(LineTotal);
            return new Summary(group.Key.VatRate, group.Key.Nature, taxable, Round(taxable * group.Key.VatRate / 100m));
        })
        .ToList();

    public static decimal Total(IEnumerable<InvoiceLine> lines) =>
        Summaries(lines).Sum(summary => summary.Taxable + summary.Tax);

    /// <summary>5-character progressive of the file name and of ProgressivoInvio, unique per invoice:
    /// base 36 of year (2 digits) and number.</summary>
    public static string Progressive(int year, int number)
    {
        const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var value = (year % 100) * 100_000L + number;
        var text = new StringBuilder();
        do
        {
            text.Insert(0, digits[(int)(value % 36)]);
            value /= 36;
        }
        while (value > 0);
        return text.ToString().PadLeft(5, '0');
    }

    /// <summary>VAT number without spaces and without the country prefix ("IT 012..." -> "012...").</summary>
    public static string VatDigits(string? vat)
    {
        var clean = new string((vat ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return clean.Length > 2 && char.IsLetter(clean[0]) && char.IsLetter(clean[1]) ? clean[2..] : clean;
    }

    public static string VatCountry(string? vat, string fallback)
    {
        var clean = new string((vat ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return clean.Length > 2 && char.IsLetter(clean[0]) && char.IsLetter(clean[1]) ? clean[..2] : fallback;
    }

    public static string FileName(CompanyProfile company, Invoice invoice) =>
        $"IT{company.FiscalCode ?? VatDigits(company.VatNumber)}_{Progressive(invoice.Year!.Value, invoice.Number!.Value)}.xml";

    /// <summary>Everything the SdI would reject, in plain Italian; empty when the invoice can be issued.</summary>
    public static List<string> Validate(CompanyProfile? company, Customer customer, CustomerFiscalData? fiscal, IReadOnlyCollection<InvoiceLine> lines)
    {
        var problems = new List<string>();
        if (company is null)
        {
            problems.Add("Configura i dati dell'azienda.");
            return problems;
        }

        var companyVat = VatDigits(company.VatNumber);
        if (companyVat.Length != 11 || !companyVat.All(char.IsAsciiDigit))
        {
            problems.Add("Azienda: partita IVA di 11 cifre mancante.");
        }

        if (string.IsNullOrWhiteSpace(company.Street) || !IsCap(company.PostalCode) || string.IsNullOrWhiteSpace(company.City) || !IsProvince(company.Province))
        {
            problems.Add("Azienda: indirizzo, CAP (5 cifre), comune e provincia (2 lettere) sono obbligatori.");
        }

        if (!string.IsNullOrWhiteSpace(company.ReaNumber) && !IsProvince(company.ReaOffice))
        {
            problems.Add("Azienda: per il numero REA indica anche la provincia dell'ufficio.");
        }

        var country = fiscal?.Country ?? "IT";
        var customerVat = VatDigits(customer.VatNumber);
        if (string.IsNullOrWhiteSpace(customerVat) && string.IsNullOrWhiteSpace(fiscal?.FiscalCode))
        {
            problems.Add($"Cliente {customer.Name}: serve partita IVA o codice fiscale.");
        }

        if (fiscal is null || string.IsNullOrWhiteSpace(fiscal.Street) || string.IsNullOrWhiteSpace(fiscal.City) ||
            (country == "IT" && (!IsCap(fiscal.PostalCode) || !IsProvince(fiscal.Province))))
        {
            problems.Add($"Cliente {customer.Name}: completa i dati fiscali (via, CAP, comune, provincia).");
        }

        if (fiscal?.SdiCode is { Length: > 0 } code && !(code.Length is 6 or 7 && code.All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c))))
        {
            problems.Add($"Cliente {customer.Name}: codice destinatario SDI non valido (7 caratteri).");
        }

        if (lines.Count == 0)
        {
            problems.Add("La fattura non ha righe.");
        }

        foreach (var line in lines.OrderBy(l => l.LineNumber))
        {
            if (string.IsNullOrWhiteSpace(line.Description) || line.Quantity <= 0 || line.UnitPrice < 0 || line.DiscountPercent is < 0 or > 100)
            {
                problems.Add($"Riga {line.LineNumber}: descrizione, quantità positiva, prezzo e sconto validi sono obbligatori.");
            }

            if (!VatRates.Contains(line.VatRate))
            {
                problems.Add($"Riga {line.LineNumber}: aliquota IVA non ammessa ({line.VatRate:0.##}%).");
            }
            else if (line.VatRate == 0 && (line.VatNature is null || !Natures.ContainsKey(line.VatNature)))
            {
                problems.Add($"Riga {line.LineNumber}: con IVA 0% indica la natura dell'operazione (es. N3.1, N6.7).");
            }
        }

        if (lines.Count > 0 && Total(lines) <= 0)
        {
            problems.Add("Il totale della fattura deve essere maggiore di zero.");
        }

        return problems;
    }

    /// <summary>The FatturaPA XML of an issued invoice. <paramref name="documents"/> are the invoiced DDTs,
    /// referenced in DatiDDT with the lines that come from each.</summary>
    public static string BuildXml(
        Invoice invoice, CompanyProfile company, Customer customer, CustomerFiscalData fiscal,
        IReadOnlyList<TransportDocument> documents)
    {
        if (invoice.Number is null || invoice.Year is null || invoice.IssueDate is null)
        {
            throw new InvalidOperationException("Solo una fattura emessa ha il file XML.");
        }

        var lines = invoice.Lines.OrderBy(line => line.LineNumber).ToList();
        var progressive = Progressive(invoice.Year.Value, invoice.Number.Value);
        var companyVat = VatDigits(company.VatNumber);
        var customerCountry = fiscal.Country is { Length: 2 } c ? c.ToUpperInvariant() : "IT";
        var foreign = customerCountry != "IT";
        var recipientCode = fiscal.SdiCode is { Length: > 0 } sdi ? sdi.ToUpperInvariant() : foreign ? "XXXXXXX" : "0000000";

        var trasmissione = new XElement("DatiTrasmissione",
            new XElement("IdTrasmittente", new XElement("IdPaese", "IT"), new XElement("IdCodice", company.FiscalCode?.ToUpperInvariant() ?? companyVat)),
            new XElement("ProgressivoInvio", progressive),
            new XElement("FormatoTrasmissione", "FPR12"),
            new XElement("CodiceDestinatario", recipientCode),
            recipientCode == "0000000" && !string.IsNullOrWhiteSpace(fiscal.Pec) ? new XElement("PECDestinatario", fiscal.Pec!.Trim()) : null);

        var cedente = new XElement("CedentePrestatore",
            new XElement("DatiAnagrafici",
                new XElement("IdFiscaleIVA", new XElement("IdPaese", "IT"), new XElement("IdCodice", companyVat)),
                string.IsNullOrWhiteSpace(company.FiscalCode) ? null : new XElement("CodiceFiscale", company.FiscalCode.Trim().ToUpperInvariant()),
                new XElement("Anagrafica", new XElement("Denominazione", Text(company.CompanyName, 80))),
                new XElement("RegimeFiscale", company.TaxRegime)),
            Sede(company.Street!, company.PostalCode!, company.City!, company.Province, "IT"),
            string.IsNullOrWhiteSpace(company.ReaNumber) ? null : new XElement("IscrizioneREA",
                new XElement("Ufficio", company.ReaOffice!.ToUpperInvariant()),
                new XElement("NumeroREA", Text(company.ReaNumber, 20)),
                new XElement("StatoLiquidazione", "LN")));

        var customerVat = VatDigits(customer.VatNumber);
        var cessionario = new XElement("CessionarioCommittente",
            new XElement("DatiAnagrafici",
                string.IsNullOrWhiteSpace(customerVat) ? null : new XElement("IdFiscaleIVA",
                    new XElement("IdPaese", VatCountry(customer.VatNumber, customerCountry)), new XElement("IdCodice", customerVat)),
                string.IsNullOrWhiteSpace(fiscal.FiscalCode) ? null : new XElement("CodiceFiscale", fiscal.FiscalCode.Trim().ToUpperInvariant()),
                new XElement("Anagrafica", new XElement("Denominazione", Text(customer.Name, 80)))),
            Sede(fiscal.Street!, foreign ? "00000" : fiscal.PostalCode!, fiscal.City!, foreign ? null : fiscal.Province, customerCountry));

        var summaries = Summaries(lines);
        var total = summaries.Sum(s => s.Taxable + s.Tax);
        var documento = new XElement("DatiGeneraliDocumento",
            new XElement("TipoDocumento", invoice.DocumentType),
            new XElement("Divisa", string.IsNullOrWhiteSpace(company.Currency) ? "EUR" : company.Currency.Trim().ToUpperInvariant()),
            new XElement("Data", invoice.IssueDate.Value.ToString("yyyy-MM-dd", Invariant)),
            new XElement("Numero", $"{invoice.Number}/{invoice.Year}"),
            new XElement("ImportoTotaleDocumento", Amount2(total)),
            string.IsNullOrWhiteSpace(invoice.Notes) ? null : new XElement("Causale", Text(invoice.Notes, 200)));

        var ddt = documents
            .Where(d => d.Number.HasValue && d.IssuedAt.HasValue)
            .OrderBy(d => d.IssuedAt)
            .Select(d => new XElement("DatiDDT",
                new XElement("NumeroDDT", $"{d.Number}/{d.Year}"),
                new XElement("DataDDT", d.IssuedAt!.Value.ToString("yyyy-MM-dd", Invariant)),
                lines.Where(l => l.TransportDocumentId == d.Id).Select(l => new XElement("RiferimentoNumeroLinea", l.LineNumber))));

        var dettaglio = lines.Select(line => new XElement("DettaglioLinee",
            new XElement("NumeroLinea", line.LineNumber),
            string.IsNullOrWhiteSpace(line.Code) ? null : new XElement("CodiceArticolo",
                new XElement("CodiceTipo", "INTERNO"), new XElement("CodiceValore", Text(line.Code, 35))),
            new XElement("Descrizione", Text(line.Description, 1000)),
            new XElement("Quantita", Decimal8(line.Quantity)),
            string.IsNullOrWhiteSpace(line.Unit) ? null : new XElement("UnitaMisura", Text(line.Unit, 10)),
            new XElement("PrezzoUnitario", Decimal8(line.UnitPrice)),
            line.DiscountPercent > 0 ? new XElement("ScontoMaggiorazione",
                new XElement("Tipo", "SC"), new XElement("Percentuale", Amount2(line.DiscountPercent))) : null,
            new XElement("PrezzoTotale", Amount2(LineTotal(line))),
            new XElement("AliquotaIVA", Amount2(line.VatRate)),
            line.VatRate == 0 ? new XElement("Natura", line.VatNature) : null));

        var riepilogo = summaries.Select(summary => new XElement("DatiRiepilogo",
            new XElement("AliquotaIVA", Amount2(summary.Rate)),
            summary.Rate == 0 ? new XElement("Natura", summary.Nature) : null,
            new XElement("ImponibileImporto", Amount2(summary.Taxable)),
            new XElement("Imposta", Amount2(summary.Tax)),
            summary.Rate > 0 ? new XElement("EsigibilitaIVA", "I") : null,
            summary.Rate == 0 ? new XElement("RiferimentoNormativo", Text(Natures[summary.Nature!], 100)) : null));

        var pagamento = new XElement("DatiPagamento",
            new XElement("CondizioniPagamento", "TP02"),
            new XElement("DettaglioPagamento",
                new XElement("ModalitaPagamento", invoice.PaymentMethod),
                invoice.PaymentDueDate is { } due ? new XElement("DataScadenzaPagamento", due.ToString("yyyy-MM-dd", Invariant)) : null,
                new XElement("ImportoPagamento", Amount2(total)),
                invoice.PaymentMethod == "MP05" && !string.IsNullOrWhiteSpace(company.Iban)
                    ? new XElement("IBAN", company.Iban.Replace(" ", string.Empty).ToUpperInvariant()) : null));

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Ns + "FatturaElettronica",
                new XAttribute("versione", "FPR12"),
                new XAttribute(XNamespace.Xmlns + "p", Ns),
                new XElement("FatturaElettronicaHeader", trasmissione, cedente, cessionario),
                new XElement("FatturaElettronicaBody",
                    new XElement("DatiGenerali", documento, ddt),
                    new XElement("DatiBeniServizi", dettaglio, riepilogo),
                    pagamento)));

        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true };
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
        {
            document.Save(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static XElement Sede(string street, string postalCode, string city, string? province, string country) =>
        new("Sede",
            new XElement("Indirizzo", Text(street, 60)),
            new XElement("CAP", postalCode),
            new XElement("Comune", Text(city, 60)),
            string.IsNullOrWhiteSpace(province) ? null : new XElement("Provincia", province.ToUpperInvariant()),
            new XElement("Nazione", country));

    /// <summary>Plain Latin text within the schema's length; control characters and anything outside
    /// Latin-1 (emoji, some symbols) are dropped instead of making the file invalid.</summary>
    private static string Text(string value, int maxLength)
    {
        var clean = new string(value.Trim().Where(c => c is >= ' ' and <= 'ÿ').ToArray());
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }

    private static string Amount2(decimal value) => Round(value).ToString("0.00", Invariant);

    private static string Decimal8(decimal value)
    {
        var text = Math.Round(value, 8, MidpointRounding.AwayFromZero).ToString("0.00######", Invariant);
        return text;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static bool IsCap(string? value) => value is { Length: 5 } && value.All(char.IsAsciiDigit);

    private static bool IsProvince(string? value) => value is { Length: 2 } && value.All(char.IsAsciiLetter);
}
