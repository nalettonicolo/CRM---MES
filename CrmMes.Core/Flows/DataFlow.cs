namespace CrmMes.Core.Flows;

/// <summary>Tipi di passo che un flusso può eseguire. V1: solo avvisare e fermarsi per un'approvazione — vedi
/// STRUMENTO-FLUSSI-DATI.md per cosa manca apposta (magazzino, assegnazione automatica, rami).</summary>
public static class DataFlowStepType
{
    /// <summary>Crea una notifica in app per ogni utente che ha il ruolo indicato in quel momento.</summary>
    public const string NotifyRole = "NotifyRole";

    /// <summary>Ferma l'esecuzione e notifica il ruolo indicato: il flusso riprende dal passo successivo solo
    /// con un'approvazione esplicita, o si ferma per sempre con un rifiuto.</summary>
    public const string RequireApproval = "RequireApproval";

    public static readonly string[] All = [NotifyRole, RequireApproval];

    public static bool IsKnown(string? type) => type is not null && All.Contains(type);
}

public static class DataFlowRunStatus
{
    public const string Running = "Running";
    public const string WaitingApproval = "WaitingApproval";
    public const string Completed = "Completed";
    public const string Rejected = "Rejected";
    public const string Failed = "Failed";
}

/// <summary>Un flusso configurato dall'Admin: quando capita l'evento <see cref="TriggerEventKey"/> (dal registro
/// in <see cref="DataFlowEvents"/>), esegue in ordine i suoi <see cref="Steps"/>.</summary>
public class DataFlowDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Chiave dell'evento che avvia il flusso, come in <see cref="DataFlowEvents"/>.</summary>
    public string TriggerEventKey { get; set; } = string.Empty;

    /// <summary>Un flusso disattivato non si avvia più, ma resta configurato: le esecuzioni passate restano.</summary>
    public bool Enabled { get; set; } = true;

    public List<DataFlowStep> Steps { get; set; } = [];

    public string? UpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Un passo di un flusso, eseguito in ordine di <see cref="Order"/>.</summary>
public class DataFlowStep
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DataFlowDefinitionId { get; set; }
    public DataFlowDefinition? DataFlowDefinition { get; set; }

    public int Order { get; set; }

    /// <summary>Uno di <see cref="DataFlowStepType"/>.</summary>
    public string Type { get; set; } = DataFlowStepType.NotifyRole;

    /// <summary>Il ruolo da avvisare (NotifyRole) o da cui serve l'approvazione (RequireApproval).</summary>
    public string TargetRole { get; set; } = string.Empty;

    /// <summary>Solo per NotifyRole: testo della notifica, con segnaposto <c>{{campo}}</c> sostituiti dai
    /// campi dell'evento (vedi <see cref="DataFlowEvents"/> per i campi disponibili per ogni evento).</summary>
    public string? MessageTemplate { get; set; }
}

/// <summary>Un'esecuzione di un <see cref="DataFlowDefinition"/>, nata da un evento reale. Tiene lo stato per
/// poter riprendere da dove si era fermata dopo un'approvazione.</summary>
public class DataFlowRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DataFlowDefinitionId { get; set; }
    public DataFlowDefinition? DataFlowDefinition { get; set; }

    public string EventKey { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }

    /// <summary>I campi dell'evento, come arrivati al momento della pubblicazione (JSON di stringa→stringa).</summary>
    public string PayloadJson { get; set; } = "{}";

    /// <summary>Uno di <see cref="DataFlowRunStatus"/>.</summary>
    public string Status { get; set; } = DataFlowRunStatus.Running;

    /// <summary>Ordine del passo in corso o in attesa; i passi con Order minore sono già stati eseguiti.</summary>
    public int CurrentStepOrder { get; set; }

    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Notifica in app per un utente specifico — non c'è ancora un servizio di invio email nel sistema
/// (serve una scelta del titolare su quale fornitore), quindi v1 dei flussi avvisa solo qui dentro.</summary>
public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public string Message { get; set; } = string.Empty;

    public Guid? DataFlowRunId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}
