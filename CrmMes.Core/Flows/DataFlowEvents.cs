namespace CrmMes.Core.Flows;

/// <summary>Un campo che un evento porta con sé, usabile come segnaposto <c>{{chiave}}</c> nel messaggio di
/// un passo NotifyRole.</summary>
public sealed record DataFlowEventField(string Key, string Label);

public sealed record DataFlowEventDefinition(string Key, string Name, IReadOnlyList<DataFlowEventField> Fields);

/// <summary>Eventi che possono far partire un flusso. Stesso ruolo del registro delle schermate dello Strumento
/// Layout (<c>FormLayoutRegistry</c>): una fonte unica, per decidere nel pannello Admin quali eventi scegliere e
/// quali segnaposto sono disponibili nel messaggio. Aggiungere un evento è pubblicare la voce qui e chiamare
/// <c>DataFlowEngine.PublishAsync</c> nel punto giusto del controller interessato.</summary>
public static class DataFlowEvents
{
    /// <summary>Una modifica tecnica dell'ufficio tecnico viene applicata: la commessa passa alla nuova
    /// revisione. Primo evento cablato, sul caso guida del titolare.</summary>
    public const string EngineeringChangeApplied = "engineering.change.applied";

    public static readonly IReadOnlyDictionary<string, DataFlowEventDefinition> All = new Dictionary<string, DataFlowEventDefinition>
    {
        [EngineeringChangeApplied] = new(
            EngineeringChangeApplied,
            "Modifica tecnica applicata (ufficio tecnico)",
            [
                new("changeNumber", "Numero della modifica"),
                new("productCode", "Codice prodotto"),
                new("productName", "Nome prodotto"),
                new("fromRevision", "Revisione precedente"),
                new("toRevision", "Nuova revisione"),
                new("requestedBy", "Richiesta da"),
            ]),
    };

    public static bool IsKnown(string? key) => key is not null && All.ContainsKey(key);
}
