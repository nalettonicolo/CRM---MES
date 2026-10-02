using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;
using CrmMes.Core.Models;

namespace CrmMes.Api.Services;

/// <summary>Reads a FatturaPA XML the company received (passive invoice) into a
/// <see cref="PurchaseInvoice"/> plus payment schedule rows. Only the fields needed for the
/// scadenziario are required; exotic blocks are ignored.</summary>
public static class FatturaPaImport
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly XNamespace Ns = FatturaPa.Ns;

    public sealed record ParsedLine(
        int LineNumber,
        string? Code,
        string Description,
        decimal Quantity,
        string Unit,
        decimal UnitPrice,
        decimal DiscountPercent,
        decimal VatRate,
        string? VatNature);

    public sealed record ParsedPayment(DateTime DueDate, decimal Amount, string? Method);

    public sealed record ParsedInvoice(
        string DocumentNumber,
        DateTime DocumentDate,
        string DocumentType,
        string SupplierName,
        string? SupplierVat,
        string? PaymentMethod,
        string Currency,
        IReadOnlyList<ParsedLine> Lines,
        IReadOnlyList<ParsedPayment> Payments,
        string ContentHash);

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public static ParsedInvoice Parse(byte[] xmlBytes)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(new MemoryStream(xmlBytes), LoadOptions.PreserveWhitespace);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("Il file non è un XML leggibile.", exception);
        }

        var body = document.Root?.Name.LocalName == "FatturaElettronica"
            ? document.Root
            : document.Descendants().FirstOrDefault(e => e.Name.LocalName == "FatturaElettronica")
              ?? throw new InvalidOperationException("Nel file non c'è una FatturaElettronica.");

        // Accept both namespaced and no-namespace exports (some intermediaries strip the default ns).
        XElement? Child(XElement parent, string local) =>
            parent.Element(Ns + local) ?? parent.Elements().FirstOrDefault(e => e.Name.LocalName == local);

        IEnumerable<XElement> Children(XElement parent, string local)
        {
            var withNamespace = parent.Elements(Ns + local).ToList();
            if (withNamespace.Count > 0)
            {
                return withNamespace;
            }

            return parent.Elements().Where(e => e.Name.LocalName == local);
        }

        string? TextOf(XElement? parent, string local) =>
            parent is null ? null : (Child(parent, local)?.Value.Trim());

        var header = Child(body, "FatturaElettronicaHeader")
            ?? throw new InvalidOperationException("Manca FatturaElettronicaHeader.");
        var bodies = Children(body, "FatturaElettronicaBody").ToList();
        if (bodies.Count == 0)
        {
            throw new InvalidOperationException("Manca FatturaElettronicaBody.");
        }

        var cedente = Child(header, "CedentePrestatore")
            ?? throw new InvalidOperationException("Manca CedentePrestatore (fornitore).");
        var anagrafica = Child(Child(cedente, "DatiAnagrafici") ?? cedente, "Anagrafica");
        var idFiscale = Child(Child(cedente, "DatiAnagrafici") ?? cedente, "IdFiscaleIVA");
        var supplierName = TextOf(anagrafica, "Denominazione")
            ?? $"{TextOf(anagrafica, "Nome")} {TextOf(anagrafica, "Cognome")}".Trim();
        if (string.IsNullOrWhiteSpace(supplierName))
        {
            throw new InvalidOperationException("Il fornitore non ha una denominazione.");
        }

        var vatId = TextOf(idFiscale, "IdCodice");
        var vatCountry = TextOf(idFiscale, "IdPaese") ?? "IT";
        var supplierVat = string.IsNullOrWhiteSpace(vatId) ? null : $"{vatCountry}{vatId}";

        var firstBody = bodies[0];
        var generali = Child(Child(firstBody, "DatiGenerali")!, "DatiGeneraliDocumento")
            ?? throw new InvalidOperationException("Mancano i dati generali del documento.");
        var documentNumber = TextOf(generali, "Numero")
            ?? throw new InvalidOperationException("Manca il numero della fattura.");
        var documentDateRaw = TextOf(generali, "Data")
            ?? throw new InvalidOperationException("Manca la data della fattura.");
        if (!DateTime.TryParse(documentDateRaw, Invariant, DateTimeStyles.AssumeUniversal, out var documentDate))
        {
            throw new InvalidOperationException($"Data fattura non valida: {documentDateRaw}.");
        }

        documentDate = DateTime.SpecifyKind(documentDate.Date, DateTimeKind.Utc);
        var documentType = TextOf(generali, "TipoDocumento") ?? "TD01";
        var currency = TextOf(generali, "Divisa") ?? "EUR";

        var lines = new List<ParsedLine>();
        foreach (var bodyEl in bodies)
        {
            var beni = Child(bodyEl, "DatiBeniServizi");
            if (beni is null)
            {
                continue;
            }

            foreach (var line in Children(beni, "DettaglioLinee"))
            {
                var n = int.TryParse(TextOf(line, "NumeroLinea"), out var num) ? num : lines.Count + 1;
                var qty = ParseDecimal(TextOf(line, "Quantita")) ?? 1m;
                var price = ParseDecimal(TextOf(line, "PrezzoUnitario")) ?? 0m;
                var discount = Children(line, "ScontoMaggiorazione")
                    .Where(s => string.Equals(TextOf(s, "Tipo"), "SC", StringComparison.OrdinalIgnoreCase))
                    .Select(s => ParseDecimal(TextOf(s, "Percentuale")) ?? 0m)
                    .DefaultIfEmpty(0m)
                    .Sum();
                var vat = ParseDecimal(TextOf(line, "AliquotaIVA")) ?? 22m;
                var code = Children(line, "CodiceArticolo").Select(c => TextOf(c, "CodiceValore")).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                lines.Add(new ParsedLine(
                    n,
                    code,
                    TextOf(line, "Descrizione") ?? $"Riga {n}",
                    qty,
                    TextOf(line, "UnitaMisura") ?? "pz",
                    price,
                    discount,
                    vat,
                    TextOf(line, "Natura")));
            }
        }

        if (lines.Count == 0)
        {
            throw new InvalidOperationException("La fattura non ha righe di dettaglio.");
        }

        var payments = new List<ParsedPayment>();
        string? paymentMethod = null;
        foreach (var bodyEl in bodies)
        {
            foreach (var pagamento in Children(bodyEl, "DatiPagamento"))
            {
                foreach (var dettaglio in Children(pagamento, "DettaglioPagamento"))
                {
                    paymentMethod ??= TextOf(dettaglio, "ModalitaPagamento");
                    var dueRaw = TextOf(dettaglio, "DataScadenzaPagamento");
                    var amount = ParseDecimal(TextOf(dettaglio, "ImportoPagamento"));
                    if (dueRaw is null || amount is null)
                    {
                        continue;
                    }

                    if (!DateTime.TryParse(dueRaw, Invariant, DateTimeStyles.AssumeUniversal, out var due))
                    {
                        continue;
                    }

                    payments.Add(new ParsedPayment(
                        DateTime.SpecifyKind(due.Date, DateTimeKind.Utc),
                        amount.Value,
                        TextOf(dettaglio, "ModalitaPagamento")));
                }
            }
        }

        if (payments.Count == 0)
        {
            var total = lines.Sum(line =>
                Math.Round(line.Quantity * line.UnitPrice * (1 - line.DiscountPercent / 100m)
                    * (1 + line.VatRate / 100m), 2, MidpointRounding.AwayFromZero));
            payments.Add(new ParsedPayment(documentDate.AddDays(30), total, paymentMethod));
        }

        return new ParsedInvoice(
            documentNumber.Trim(),
            documentDate,
            documentType.Trim(),
            supplierName.Trim(),
            supplierVat,
            paymentMethod,
            currency,
            lines,
            payments,
            Hash(xmlBytes));
    }

    private static decimal? ParseDecimal(string? text) =>
        decimal.TryParse(text, NumberStyles.Number, Invariant, out var value) ? value : null;
}
