using System.Security.Claims;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using CrmMes.Core.Layout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Strumento Layout: legge e salva l'ordine, l'etichetta, la visibilità e l'obbligatorietà dei campi
/// delle schermate gestite. Leggere è aperto a tutti gli utenti autenticati (ogni schermata ne ha bisogno per
/// mostrarsi); modificare è riservato all'Admin e ai ruoli che l'Admin ha autorizzato. Un campo non personalizzato
/// resta con i valori di default del codice.</summary>
[ApiController]
[Authorize]
[Route("api/layout")]
public class FormLayoutController(ApplicationDbContext db) : ControllerBase
{
    /// <summary>Campi personalizzati di una schermata (oltre ai predefiniti nel codice). Aperto a tutti gli
    /// utenti autenticati: ogni modulo ne ha bisogno per disegnarsi, come per i campi predefiniti.</summary>
    [HttpGet("{screen}/custom-fields")]
    public async Task<ActionResult<List<CustomFieldDefinitionResponse>>> GetCustomFields(string screen, CancellationToken cancellationToken = default)
    {
        if (!FormLayoutRegistry.IsKnown(screen))
        {
            return NotFound(new { message = "Schermata non gestita dallo strumento Layout." });
        }

        var fields = await db.CustomFieldDefinitions.AsNoTracking()
            .Where(f => f.Screen == screen).OrderBy(f => f.Order).ToListAsync(cancellationToken);
        return Ok(fields.Select(ToCustomFieldResponse).ToList());
    }

    /// <summary>Aggiunge un campo nuovo di sana pianta alla schermata (es. "Giorni di pagamento" su "Nuovo
    /// fornitore"): a differenza degli altri campi dello Strumento Layout, questo non esiste nel modello, il suo
    /// valore si salva per record in CustomFieldValue (vedi CustomFieldService).</summary>
    [HttpPost("{screen}/custom-fields")]
    public async Task<ActionResult<CustomFieldDefinitionResponse>> CreateCustomField(
        string screen, SaveCustomFieldRequest request, CancellationToken cancellationToken = default)
    {
        var forbidden = await EnsureCanEditAsync(cancellationToken);
        if (forbidden is not null)
        {
            return forbidden;
        }

        if (!FormLayoutRegistry.IsKnown(screen))
        {
            return NotFound(new { message = "Schermata non gestita dallo strumento Layout." });
        }

        var label = request.Label?.Trim();
        if (string.IsNullOrWhiteSpace(label))
        {
            return BadRequest(new { message = "L'etichetta del campo è obbligatoria." });
        }

        if (label.Length > 120)
        {
            return BadRequest(new { message = "L'etichetta non può superare 120 caratteri." });
        }

        var fieldType = string.IsNullOrWhiteSpace(request.FieldType) ? CustomFieldType.Text : request.FieldType.Trim();
        if (!CustomFieldType.IsKnown(fieldType))
        {
            return BadRequest(new { message = $"Tipo di campo sconosciuto. Valori ammessi: {string.Join(", ", CustomFieldType.All)}." });
        }

        var key = Slugify(label);
        if (await db.CustomFieldDefinitions.AnyAsync(f => f.Screen == screen && f.Key == key, cancellationToken))
        {
            return Conflict(new { message = $"Esiste già un campo personalizzato con chiave '{key}' su questa schermata: scegli un'etichetta diversa." });
        }

        var maxOrder = await db.CustomFieldDefinitions.Where(f => f.Screen == screen)
            .Select(f => (int?)f.Order).MaxAsync(cancellationToken) ?? 0;

        var field = new CustomFieldDefinition
        {
            Screen = screen,
            Key = key,
            Label = label,
            FieldType = fieldType,
            Order = maxOrder + 1,
            Required = request.Required,
            UpdatedBy = User.FindFirstValue(ClaimTypes.Name),
            UpdatedAt = DateTime.UtcNow,
        };
        db.CustomFieldDefinitions.Add(field);
        db.AuditLogs.Add(new AuditLog
        {
            Action = "CustomFieldCreated",
            EntityType = "CustomFieldDefinition",
            EntityId = field.Id,
            UserName = field.UpdatedBy,
            Details = $"Campo personalizzato '{field.Label}' ({field.FieldType}) aggiunto a {screen}."
        });
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetCustomFields), new { screen }, ToCustomFieldResponse(field));
    }

    /// <summary>Etichetta, ordine e obbligatorietà sono modificabili dopo la creazione; tipo e schermata no,
    /// perché cambierebbero il significato dei valori già salvati per i record esistenti.</summary>
    [HttpPut("{screen}/custom-fields/{id:guid}")]
    public async Task<ActionResult<CustomFieldDefinitionResponse>> EditCustomField(
        string screen, Guid id, SaveCustomFieldRequest request, CancellationToken cancellationToken = default)
    {
        var forbidden = await EnsureCanEditAsync(cancellationToken);
        if (forbidden is not null)
        {
            return forbidden;
        }

        var field = await db.CustomFieldDefinitions.SingleOrDefaultAsync(f => f.Id == id && f.Screen == screen, cancellationToken);
        if (field is null)
        {
            return NotFound();
        }

        var label = request.Label?.Trim();
        if (string.IsNullOrWhiteSpace(label))
        {
            return BadRequest(new { message = "L'etichetta del campo è obbligatoria." });
        }

        if (label.Length > 120)
        {
            return BadRequest(new { message = "L'etichetta non può superare 120 caratteri." });
        }

        field.Label = label;
        field.Order = request.Order;
        field.Required = request.Required;
        field.UpdatedBy = User.FindFirstValue(ClaimTypes.Name);
        field.UpdatedAt = DateTime.UtcNow;
        db.AuditLogs.Add(new AuditLog
        {
            Action = "CustomFieldUpdated",
            EntityType = "CustomFieldDefinition",
            EntityId = field.Id,
            UserName = field.UpdatedBy,
            Details = $"Campo personalizzato '{field.Label}' modificato su {screen}."
        });
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToCustomFieldResponse(field));
    }

    /// <summary>Rimuove il campo e, in cascata, tutti i valori già salvati per i record esistenti.</summary>
    [HttpDelete("{screen}/custom-fields/{id:guid}")]
    public async Task<IActionResult> DeleteCustomField(string screen, Guid id, CancellationToken cancellationToken = default)
    {
        var forbidden = await EnsureCanEditAsync(cancellationToken);
        if (forbidden is not null)
        {
            return forbidden;
        }

        var field = await db.CustomFieldDefinitions.SingleOrDefaultAsync(f => f.Id == id && f.Screen == screen, cancellationToken);
        if (field is null)
        {
            return NotFound();
        }

        db.CustomFieldDefinitions.Remove(field);
        db.AuditLogs.Add(new AuditLog
        {
            Action = "CustomFieldDeleted",
            EntityType = "CustomFieldDefinition",
            EntityId = field.Id,
            UserName = User.FindFirstValue(ClaimTypes.Name),
            Details = $"Campo personalizzato '{field.Label}' rimosso da {screen}."
        });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<ActionResult?> EnsureCanEditAsync(CancellationToken cancellationToken)
    {
        var granted = await LoadGrantedRolesAsync(cancellationToken);
        return FormLayoutAccess.CanEdit(User.FindFirstValue(ClaimTypes.Role), string.Join(',', granted)) ? null : Forbid();
    }

    /// <summary>Slug leggibile e stabile dall'etichetta (es. "Giorni di pagamento" → "giorni-di-pagamento"),
    /// usato come chiave: non cambia anche se l'etichetta viene poi modificata.</summary>
    private static string Slugify(string label)
    {
        var slug = new string(label.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--"))
        {
            slug = slug.Replace("--", "-");
        }

        slug = slug.Trim('-');
        return slug.Length > 80 ? slug[..80] : (slug.Length == 0 ? Guid.NewGuid().ToString("N")[..8] : slug);
    }

    private static CustomFieldDefinitionResponse ToCustomFieldResponse(CustomFieldDefinition field) =>
        new(field.Id, field.Key, field.Label, field.FieldType, field.Order, field.Required);

    /// <summary>Chi può modificare i layout, per l'utente che chiede: serve ai client per mostrare o nascondere
    /// i comandi di modifica. Aperto a tutti gli utenti autenticati.</summary>
    [HttpGet("access")]
    public async Task<ActionResult<LayoutAccessResponse>> GetAccess(CancellationToken cancellationToken = default)
    {
        var granted = await LoadGrantedRolesAsync(cancellationToken);
        var role = User.FindFirstValue(ClaimTypes.Role);
        return Ok(new LayoutAccessResponse(
            FormLayoutAccess.CanEdit(role, string.Join(',', granted)),
            granted,
            FormLayoutAccess.GrantableRoles.ToList()));
    }

    /// <summary>Ruoli autorizzati a modificare i layout (solo Admin). Admin è sempre autorizzato e non va inviato.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPut("access")]
    public async Task<ActionResult<LayoutAccessResponse>> SaveAccess(SaveLayoutAccessRequest request, CancellationToken cancellationToken = default)
    {
        var requested = request.Roles ?? [];
        var unknown = requested.FirstOrDefault(r => !FormLayoutAccess.GrantableRoles.Contains(r));
        if (unknown is not null)
        {
            return BadRequest(new { message = $"Il ruolo '{unknown}' non può essere autorizzato allo strumento Layout." });
        }

        var profile = await db.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            return Conflict(new { message = "Configura prima l'azienda (ragione sociale e settore)." });
        }

        var granted = FormLayoutAccess.Parse(FormLayoutAccess.Format(requested));
        profile.LayoutEditorRoles = FormLayoutAccess.Format(granted);
        profile.UpdatedAt = DateTime.UtcNow;
        db.AuditLogs.Add(new AuditLog
        {
            Action = "LayoutAccessSaved",
            EntityType = "CompanyProfile",
            EntityId = profile.Id,
            UserName = User.FindFirstValue(ClaimTypes.Name),
            Details = $"Ruoli che possono modificare i layout: {(granted.Count == 0 ? "solo Admin" : profile.LayoutEditorRoles)}"
        });
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new LayoutAccessResponse(true, granted.ToList(), FormLayoutAccess.GrantableRoles.ToList()));
    }

    /// <summary>Elenco di tutte le schermate gestite dallo strumento Layout, già con le personalizzazioni
    /// salvate (non solo i campi di default): chi la usa come fonte dei dati, non solo come elenco nomi,
    /// deve vedere lo stato vero.</summary>
    [HttpGet]
    public async Task<ActionResult<List<LayoutScreenResponse>>> List(CancellationToken cancellationToken = default)
    {
        var saved = await db.FormFieldSettings.AsNoTracking().ToListAsync(cancellationToken);
        var byScreen = saved.GroupBy(s => s.Screen).ToDictionary(g => g.Key, g => (IReadOnlyList<FormFieldSetting>)g.ToList());
        return Ok(FormLayoutRegistry.Screens.Keys.OrderBy(k => k)
            .Select(screen => Merge(screen, byScreen.GetValueOrDefault(screen, [])))
            .ToList());
    }

    [HttpGet("{screen}")]
    public async Task<ActionResult<LayoutScreenResponse>> Get(string screen, CancellationToken cancellationToken = default)
    {
        if (!FormLayoutRegistry.IsKnown(screen))
        {
            return NotFound(new { message = "Schermata non gestita dallo strumento Layout." });
        }

        var saved = await db.FormFieldSettings.AsNoTracking().Where(f => f.Screen == screen).ToListAsync(cancellationToken);
        return Ok(Merge(screen, saved));
    }

    [HttpPut("{screen}")]
    public async Task<ActionResult<LayoutScreenResponse>> Save(string screen, SaveLayoutRequest request, CancellationToken cancellationToken = default)
    {
        var granted = await LoadGrantedRolesAsync(cancellationToken);
        if (!FormLayoutAccess.CanEdit(User.FindFirstValue(ClaimTypes.Role), string.Join(',', granted)))
        {
            return Forbid();
        }

        if (!FormLayoutRegistry.IsKnown(screen))
        {
            return NotFound(new { message = "Schermata non gestita dallo strumento Layout." });
        }

        var defaults = FormLayoutRegistry.Screens[screen].ToDictionary(f => f.Key);
        var requested = request.Fields ?? [];
        if (requested.Any(f => !defaults.ContainsKey(f.FieldKey)))
        {
            return BadRequest(new { message = "Campo sconosciuto per questa schermata." });
        }

        if (requested.Select(f => f.FieldKey).Distinct().Count() != requested.Count)
        {
            return BadRequest(new { message = "Ogni campo può comparire una sola volta." });
        }

        var user = User.FindFirstValue(ClaimTypes.Name);
        var existing = await db.FormFieldSettings.Where(f => f.Screen == screen).ToListAsync(cancellationToken);
        db.FormFieldSettings.RemoveRange(existing);

        foreach (var item in requested)
        {
            var def = defaults[item.FieldKey];
            var label = string.IsNullOrWhiteSpace(item.Label) ? null : item.Label.Trim();
            if (label is not null && label.Length > 120)
            {
                return BadRequest(new { message = "L'etichetta non può superare 120 caratteri." });
            }

            db.FormFieldSettings.Add(new FormFieldSetting
            {
                Screen = screen,
                FieldKey = item.FieldKey,
                Label = label == def.DefaultLabel ? null : label,
                Order = item.Order,
                // Un campo non nascondibile resta sempre visibile, e l'oggetto della richiesta resta obbligatorio.
                Visible = def.CanHide ? item.Visible : true,
                Required = FormLayoutRegistry.RequiredFor(def, item.Required),
                UpdatedBy = user,
                UpdatedAt = DateTime.UtcNow,
            });
        }

        db.AuditLogs.Add(new AuditLog
        {
            Action = "FormLayoutSaved",
            EntityType = "FormFieldSetting",
            EntityId = null,
            UserName = user,
            Details = requested.Count == 0
                ? $"Layout di {screen} ripristinato ai valori predefiniti."
                : $"Layout di {screen} salvato: {requested.Count} campi personalizzati."
        });
        await db.SaveChangesAsync(cancellationToken);
        var saved = await db.FormFieldSettings.AsNoTracking().Where(f => f.Screen == screen).ToListAsync(cancellationToken);
        return Ok(Merge(screen, saved));
    }

    private async Task<List<string>> LoadGrantedRolesAsync(CancellationToken cancellationToken)
    {
        var stored = await db.CompanyProfiles.AsNoTracking().Select(p => p.LayoutEditorRoles).FirstOrDefaultAsync(cancellationToken);
        return FormLayoutAccess.Parse(stored).ToList();
    }

    /// <summary>Unisce i default del codice con le personalizzazioni salvate, in ordine.</summary>
    internal static LayoutScreenResponse Merge(string screen, IReadOnlyList<FormFieldSetting> saved)
    {
        var byKey = saved.ToDictionary(s => s.FieldKey);
        var fields = FormLayoutRegistry.Screens[screen]
            .Select(def =>
            {
                var s = byKey.GetValueOrDefault(def.Key);
                return new LayoutFieldResponse(
                    def.Key,
                    def.DefaultLabel,
                    s?.Label ?? def.DefaultLabel,
                    s?.Order ?? def.Order,
                    s?.Visible ?? true,
                    FormLayoutRegistry.RequiredFor(def, s?.Required ?? def.Required),
                    def.CanHide,
                    def.Required);
            })
            .OrderBy(f => f.Order)
            .ToList();
        return new LayoutScreenResponse(screen, FormLayoutRegistry.Names[screen], fields);
    }
}

public sealed record LayoutFieldResponse(
    string Key, string DefaultLabel, string Label, int Order, bool Visible, bool Required, bool CanHide, bool DefaultRequired);

public sealed record LayoutScreenResponse(string Screen, string Name, List<LayoutFieldResponse> Fields);

public sealed record LayoutAccessResponse(bool CanEdit, List<string> GrantedRoles, List<string> GrantableRoles);

public sealed record SaveLayoutAccessRequest(List<string>? Roles);

public sealed record SaveLayoutRequest(List<SaveLayoutFieldRequest>? Fields);

public sealed record SaveLayoutFieldRequest(string FieldKey, string? Label, int Order, bool Visible, bool Required);

public sealed record CustomFieldDefinitionResponse(Guid Id, string Key, string Label, string FieldType, int Order, bool Required);

public sealed record SaveCustomFieldRequest(string? Label, string? FieldType, int Order, bool Required);
