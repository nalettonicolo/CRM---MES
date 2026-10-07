namespace CrmMes.Core.Layout;

/// <summary>Tipi di valore di un campo personalizzato: determina come viene validato e mostrato nei client.</summary>
public static class CustomFieldType
{
    public const string Text = "Text";
    public const string Number = "Number";
    public const string Date = "Date";
    public const string Checkbox = "Checkbox";

    public static readonly string[] All = [Text, Number, Date, Checkbox];

    public static bool IsKnown(string? type) => type is not null && All.Contains(type);
}

/// <summary>Campo aggiunto dall'Admin a una schermata dello Strumento Layout, oltre ai campi predefiniti nel
/// codice (<see cref="FormLayoutRegistry"/>). A differenza di <see cref="FormFieldSetting"/> — che personalizza
/// l'etichetta/ordine/visibilità di un campo che esiste già nel modello — un campo personalizzato non ha una
/// colonna propria nelle tabelle: il suo valore per ogni record si salva in <see cref="CustomFieldValue"/>.
/// Esempio: "Tipologia di pagamento" o "Giorni di pagamento" su fornitori e clienti.</summary>
public class CustomFieldDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Chiave della schermata, come in FormLayoutRegistry (es. "suppliers.new").</summary>
    public string Screen { get; set; } = string.Empty;

    /// <summary>Slug univoco per schermata (es. "giorni-pagamento"), usato per salvare e leggere i valori:
    /// non cambia anche se l'etichetta viene poi modificata.</summary>
    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
    public string FieldType { get; set; } = CustomFieldType.Text;
    public int Order { get; set; }
    public bool Required { get; set; }

    public string? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Valore di un campo personalizzato per un record di un'entità qualunque (fornitore, cliente...): niente
/// chiave esterna verso l'entità, perché lo stesso campo (per Screen) può valere per più tipi di entità diversi
/// nello stesso modulo, e per tipi di entità diversi non c'è una tabella comune a cui agganciarsi.</summary>
public class CustomFieldValue
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FieldDefinitionId { get; set; }
    public CustomFieldDefinition FieldDefinition { get; set; } = null!;

    /// <summary>Es. "Supplier", "Customer". Insieme a EntityId individua il record.</summary>
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }

    public string? Value { get; set; }
}
