using System.Globalization;

namespace CrmMes.Web.Services;

/// <summary>Italian names, colours and formats shared by every page.</summary>
public static class Labels
{
    public static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

    public static string Role(string role) => role switch
    {
        "Admin" => "Amministratore",
        "Management" => "Direzione",
        "Sales" => "Commerciale",
        "Purchasing" => "Acquisti",
        "Warehouse" => "Magazzino",
        "Operator" => "Operatore",
        _ => role,
    };

    public static string WorkOrderStatus(string status) => status switch
    {
        "Draft" => "Bozza",
        "Released" => "Rilasciata",
        "InProgress" => "In lavorazione",
        "Completed" => "Completata",
        "Cancelled" => "Annullata",
        _ => status,
    };

    /// <summary>Badge colour: blue waiting to start, amber being worked on, green done, red cancelled.</summary>
    public static string WorkOrderStatusClass(string status) => status switch
    {
        "Released" => "info",
        "InProgress" => "warn",
        "Completed" => "ok",
        "Cancelled" => "danger",
        _ => string.Empty,
    };

    public static string OperationStatus(string status) => status switch
    {
        "Pending" => "Da fare",
        "InProgress" => "In corso",
        "Completed" => "Completata",
        _ => status,
    };

    public static string OperationStatusClass(string status) => status switch
    {
        "InProgress" => "warn",
        "Completed" => "ok",
        _ => string.Empty,
    };

    public static string QuoteStatus(string status, DateTime? convertedAt = null) => convertedAt is not null ? "Convertito" : status switch
    {
        "Draft" => "Bozza",
        "Sent" => "Inviato",
        "Accepted" => "Accettato",
        "Rejected" => "Rifiutato",
        _ => status,
    };

    public static string QuoteStatusClass(string status, DateTime? convertedAt = null) => convertedAt is not null ? "ok" : status switch
    {
        "Sent" => "info",
        "Accepted" => "ok",
        "Rejected" => "danger",
        _ => string.Empty,
    };

    public static string Money(decimal value) => value.ToString("C", Italian);

    /// <summary>Transport documents and invoices share the same life: draft, issued (numbered), cancelled.</summary>
    public static string DocumentStatus(string status) => status switch
    {
        "Draft" => "Bozza",
        "Issued" => "Emesso",
        "Cancelled" => "Annullato",
        _ => status,
    };

    public static string DocumentStatusClass(string status) => status switch
    {
        "Issued" => "ok",
        "Cancelled" => "danger",
        _ => string.Empty,
    };

    public static string PurchaseOrderStatus(string status) => status switch
    {
        "Draft" => "Bozza",
        "Confirmed" => "Confermato",
        "PartiallyReceived" => "Ricevuto in parte",
        "Received" => "Ricevuto",
        "Cancelled" => "Annullato",
        _ => status,
    };

    public static string PurchaseOrderStatusClass(string status) => status switch
    {
        "Confirmed" => "info",
        "PartiallyReceived" => "warn",
        "Received" => "ok",
        "Cancelled" => "danger",
        _ => string.Empty,
    };

    public static string MissingSource(string source) => source switch
    {
        "MinStock" => "Scorta minima",
        "Manual" => "Segnalazione manuale",
        _ => source,
    };

    public static string InvoiceType(string documentType) => documentType switch
    {
        "TD24" => "Differita (da DDT)",
        "TD01" => "Immediata",
        _ => documentType,
    };

    public static string TransportBy(string transportBy) => transportBy switch
    {
        "Sender" => "Mittente",
        "Recipient" => "Destinatario",
        "Carrier" => "Vettore",
        _ => transportBy,
    };

    public static string Channel(string channel) => channel switch
    {
        "desktop" => "Desktop",
        "web" => "Web",
        "mobile" => "Telefono",
        _ => channel,
    };

    public static string Date(DateTime? value) =>
        value is null ? "—" : value.Value.ToLocalTime().ToString("dd/MM/yyyy", Italian);

    public static string DateTime(DateTime? value) =>
        value is null ? "—" : value.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Italian);

    public static string Number(decimal value) => value.ToString(value == decimal.Truncate(value) ? "N0" : "N2", Italian);

    public static string Percent(decimal? ratio) => ratio is null ? "—" : (ratio.Value * 100).ToString("N0", Italian) + "%";

    /// <summary>Width class (w0…w100, steps of 10) for a progress bar: the CSP forbids inline styles.</summary>
    public static string ProgressClass(int done, int total)
    {
        if (total <= 0)
        {
            return "w0";
        }

        var step = (int)Math.Round(Math.Clamp((double)done / total, 0, 1) * 10, MidpointRounding.AwayFromZero);
        return "w" + step * 10;
    }

    /// <summary>Overdue: a due date already past on an order not finished.</summary>
    public static bool IsOverdue(DateTime? dueDate, string status, DateTime now) =>
        dueDate is not null && status is not ("Completed" or "Cancelled") && dueDate.Value.ToLocalTime().Date < now.ToLocalTime().Date;
}
