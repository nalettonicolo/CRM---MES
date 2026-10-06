namespace CrmMes.Core.Layout;

/// <summary>Impostazioni di un campo di una schermata, modificabili dall'Admin dallo strumento Layout:
/// etichetta mostrata, ordine, visibilità e obbligatorietà. Le schermate che usano il modello hanno un
/// insieme di campi di default nel codice (<see cref="FormLayoutRegistry"/>); una riga salvata sovrascrive
/// solo ciò che l'Admin ha cambiato.</summary>
public class FormFieldSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Chiave della schermata, es. "service.request".</summary>
    public string Screen { get; set; } = string.Empty;

    /// <summary>Chiave del campo dentro la schermata, es. "subject".</summary>
    public string FieldKey { get; set; } = string.Empty;

    /// <summary>Etichetta personalizzata; null = etichetta di default.</summary>
    public string? Label { get; set; }

    public int Order { get; set; }
    public bool Visible { get; set; } = true;
    public bool Required { get; set; }

    public string? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Campo di default di una schermata: il punto di partenza prima di qualunque personalizzazione.</summary>
public sealed record FormFieldDefault(string Key, string DefaultLabel, int Order, bool Required, bool CanHide);

/// <summary>Schermate che lo strumento Layout sa gestire. Aggiungerne una significa dichiararne i campi qui
/// e farla leggere dalla propria pagina con il servizio di layout.</summary>
public static class FormLayoutRegistry
{
    public const string ServiceRequest = "service.request";
    public const string CustomerNew = "customers.new";
    public const string MaintenanceNew = "maintenance.new";
    public const string EnergyProjectNew = "energy.project.new";
    public const string ProductNew = "products.new";
    public const string MaterialNew = "materials.new";
    public const string EquipmentNew = "equipment.new";
    public const string SupplierNew = "suppliers.new";
    public const string PurchaseOrderNew = "purchaseOrder.new";
    public const string QuoteNew = "quote.new";
    public const string InvoiceNew = "invoice.new";
    public const string QuoteConvert = "quote.convert";

    /// <summary>Nome leggibile della schermata, per l'elenco nello strumento Layout.</summary>
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        [ServiceRequest] = "Nuova richiesta di assistenza (Service)",
        [CustomerNew] = "Nuovo cliente (Vendite)",
        [MaintenanceNew] = "Nuovo intervento di manutenzione",
        [EnergyProjectNew] = "Nuovo progetto di efficientamento energetico",
        [ProductNew] = "Nuovo prodotto (distinta e cicli)",
        [MaterialNew] = "Nuovo materiale (magazzino)",
        [EquipmentNew] = "Nuova macchina (manutenzione)",
        [SupplierNew] = "Nuovo fornitore (acquisti)",
        [PurchaseOrderNew] = "Nuovo ordine fornitore (acquisti)",
        [QuoteNew] = "Nuovo preventivo (vendite)",
        [InvoiceNew] = "Nuova fattura elettronica (testata)",
        [QuoteConvert] = "Conversione preventivo in commesse (web)",
    };

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<FormFieldDefault>> Screens =
        new Dictionary<string, IReadOnlyList<FormFieldDefault>>
        {
            [ProductNew] =
            [
                new("code", "Codice", 1, Required: true, CanHide: false),
                new("name", "Nome", 2, Required: true, CanHide: false),
                new("description", "Descrizione", 3, Required: false, CanHide: true),
            ],
            [QuoteConvert] =
            [
                new("area", "Area di produzione", 1, Required: false, CanHide: true),
                new("dueDate", "Consegna prevista", 2, Required: false, CanHide: true),
            ],
            [InvoiceNew] =
            [
                new("customer", "Cliente", 1, Required: true, CanHide: false),
                new("payment", "Pagamento", 2, Required: false, CanHide: true),
                new("dueDate", "Scadenza", 3, Required: false, CanHide: true),
                new("notes", "Causale / note", 4, Required: false, CanHide: true),
            ],
            [QuoteNew] =
            [
                new("customer", "Cliente", 1, Required: true, CanHide: false),
                new("validUntil", "Valido fino al", 2, Required: false, CanHide: true),
                new("notes", "Note per il cliente", 3, Required: false, CanHide: true),
                new("lineDescription", "Descrizione", 4, Required: true, CanHide: false),
                new("lineQuantity", "Q.tà", 5, Required: true, CanHide: false),
                new("linePrice", "Prezzo unit. €", 6, Required: true, CanHide: false),
                new("lineDiscount", "Sconto %", 7, Required: false, CanHide: true),
            ],
            [PurchaseOrderNew] =
            [
                new("supplier", "Fornitore", 1, Required: true, CanHide: false),
                new("itemCode", "Codice", 2, Required: true, CanHide: false),
                new("itemQuantity", "Quantità", 3, Required: true, CanHide: false),
                new("itemPrice", "Prezzo", 4, Required: false, CanHide: true),
            ],
            [SupplierNew] =
            [
                new("name", "Nome", 1, Required: true, CanHide: false),
                new("code", "Codice", 2, Required: true, CanHide: false),
                new("email", "Email", 3, Required: false, CanHide: true),
                new("phone", "Telefono", 4, Required: false, CanHide: true),
                new("website", "Sito web / catalogo", 5, Required: false, CanHide: true),
            ],
            [MaterialNew] =
            [
                new("code", "Codice", 1, Required: true, CanHide: false),
                new("name", "Descrizione", 2, Required: true, CanHide: false),
                new("unit", "Unità di misura", 3, Required: false, CanHide: true),
                new("stock", "Giacenza iniziale", 4, Required: false, CanHide: true),
                new("minStock", "Scorta minima", 5, Required: false, CanHide: true),
            ],
            [EquipmentNew] =
            [
                new("name", "Nome", 1, Required: true, CanHide: false),
                new("code", "Codice", 2, Required: true, CanHide: false),
                new("workCenter", "Centro di lavoro", 3, Required: false, CanHide: true),
            ],
            [EnergyProjectNew] =
            [
                new("equipment", "Macchina", 1, Required: true, CanHide: false),
                new("title", "Titolo", 2, Required: true, CanHide: false),
                new("baselineFrom", "Ex ante dal", 3, Required: true, CanHide: false),
                new("baselineTo", "Ex ante al", 4, Required: true, CanHide: false),
                new("description", "Descrizione", 5, Required: false, CanHide: true),
            ],
            [MaintenanceNew] =
            [
                new("equipment", "Macchina", 1, Required: true, CanHide: false),
                new("title", "Titolo", 2, Required: true, CanHide: false),
                new("type", "Tipo", 3, Required: true, CanHide: false),
                new("dueDate", "Scadenza", 4, Required: false, CanHide: true),
                new("recurrence", "Ricorrenza (giorni)", 5, Required: false, CanHide: true),
            ],
            [CustomerNew] =
            [
                new("code", "Codice", 1, Required: true, CanHide: false),
                new("name", "Ragione sociale", 2, Required: true, CanHide: false),
                new("vatNumber", "Partita IVA", 3, Required: false, CanHide: true),
            ],
            [ServiceRequest] =
            [
                new("subject", "Oggetto", 1, Required: true, CanHide: false),
                new("description", "Descrizione", 2, Required: false, CanHide: true),
                new("priority", "Priorità", 3, Required: true, CanHide: false),
                new("channel", "Canale", 4, Required: true, CanHide: false),
                new("requestedBy", "Richiesta da", 5, Required: false, CanHide: true),
                new("contactInfo", "Contatto", 6, Required: false, CanHide: true),
            ],
        };

    public static bool IsKnown(string screen) => Screens.ContainsKey(screen);

    /// <summary>Regola unica per tutte le schermate: un campo che non si può nascondere ed è obbligatorio
    /// per default resta obbligatorio. Vale per l'oggetto della richiesta come per il codice cliente.</summary>
    public static bool RequiredFor(FormFieldDefault def, bool requested) => (!def.CanHide && def.Required) || requested;
}
