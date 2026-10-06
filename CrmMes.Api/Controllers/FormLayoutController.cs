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

    /// <summary>Elenco di tutte le schermate gestite dallo strumento Layout, con i loro campi di default.</summary>
    [HttpGet]
    public ActionResult<List<LayoutScreenResponse>> List()
    {
        var saved = new List<FormFieldSetting>();
        return Ok(FormLayoutRegistry.Screens.Keys.OrderBy(k => k)
            .Select(screen => Merge(screen, saved))
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
