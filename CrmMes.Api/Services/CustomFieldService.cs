using CrmMes.Core.Data;
using CrmMes.Core.Layout;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Services;

/// <summary>Legge e salva i campi personalizzati aggiunti dall'Admin a una schermata (oltre ai campi predefiniti
/// nel codice). Lo stesso campo (per Screen) può valere per più tipi di entità: es. "Giorni di pagamento" sia
/// per i fornitori sia per i clienti, una definizione su schermate diverse per ciascuno.</summary>
public sealed class CustomFieldService(ApplicationDbContext db)
{
    public Task<List<CustomFieldDefinition>> GetDefinitionsAsync(string screen, CancellationToken cancellationToken = default) =>
        db.CustomFieldDefinitions.AsNoTracking().Where(f => f.Screen == screen).OrderBy(f => f.Order).ToListAsync(cancellationToken);

    /// <summary>Valori dei campi personalizzati di un record, per chiave invece che per Id: più comodo per i
    /// client, che conoscono le chiavi dalla lista dei campi della schermata.</summary>
    public async Task<Dictionary<string, string?>> GetValuesAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default)
    {
        var rows = await db.CustomFieldValues.AsNoTracking()
            .Include(v => v.FieldDefinition)
            .Where(v => v.EntityType == entityType && v.EntityId == entityId)
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(v => v.FieldDefinition.Key, v => v.Value);
    }

    /// <summary>Valida i campi personalizzati obbligatori e di tipo Number della schermata, poi prepara
    /// l'inserimento/aggiornamento/cancellazione delle righe nello stesso DbContext del chiamante — che deve
    /// comunque chiamare SaveChangesAsync (di solito insieme al resto della richiesta, nella stessa transazione
    /// implicita). Torna il messaggio di errore da mostrare, o null se tutto è a posto.</summary>
    public async Task<string?> ValidateAndStageAsync(
        string screen, string entityType, Guid entityId, IReadOnlyDictionary<string, string?>? values, CancellationToken cancellationToken = default)
    {
        var definitions = await GetDefinitionsAsync(screen, cancellationToken);
        if (definitions.Count == 0)
        {
            return null;
        }

        values ??= new Dictionary<string, string?>();

        var missing = definitions
            .Where(def => def.Required && string.IsNullOrWhiteSpace(values.GetValueOrDefault(def.Key)))
            .Select(def => def.Label)
            .ToList();
        if (missing.Count > 0)
        {
            return "Compila i campi obbligatori: " + string.Join(", ", missing) + ".";
        }

        foreach (var def in definitions)
        {
            var value = values.GetValueOrDefault(def.Key);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (def.FieldType == CustomFieldType.Number && !decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                return $"Il campo '{def.Label}' deve essere un numero.";
            }

            if (def.FieldType == CustomFieldType.Date && !DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
            {
                return $"Il campo '{def.Label}' deve essere una data valida.";
            }
        }

        var existing = await db.CustomFieldValues
            .Where(v => v.EntityType == entityType && v.EntityId == entityId)
            .ToListAsync(cancellationToken);

        foreach (var def in definitions)
        {
            var value = values.GetValueOrDefault(def.Key);
            var row = existing.FirstOrDefault(v => v.FieldDefinitionId == def.Id);
            if (string.IsNullOrWhiteSpace(value))
            {
                if (row is not null)
                {
                    db.CustomFieldValues.Remove(row);
                }

                continue;
            }

            if (row is null)
            {
                db.CustomFieldValues.Add(new CustomFieldValue
                {
                    FieldDefinitionId = def.Id,
                    EntityType = entityType,
                    EntityId = entityId,
                    Value = value.Trim()
                });
            }
            else
            {
                row.Value = value.Trim();
            }
        }

        return null;
    }
}
