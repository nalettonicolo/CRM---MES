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

    /// <summary>Nome leggibile della schermata, per l'elenco nello strumento Layout.</summary>
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        [ServiceRequest] = "Nuova richiesta di assistenza (Service)",
        [CustomerNew] = "Nuovo cliente (Vendite)",
        [MaintenanceNew] = "Nuovo intervento di manutenzione",
    };

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<FormFieldDefault>> Screens =
        new Dictionary<string, IReadOnlyList<FormFieldDefault>>
        {
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
