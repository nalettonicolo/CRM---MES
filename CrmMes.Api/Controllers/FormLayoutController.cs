using System.Security.Claims;
using CrmMes.Core.Data;
using CrmMes.Core.Layout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Strumento Layout: legge e salva l'ordine, l'etichetta, la visibilità e l'obbligatorietà dei campi
/// delle schermate gestite. Leggere è aperto a tutti gli utenti autenticati (ogni schermata ne ha bisogno per
/// mostrarsi); modificare è solo Admin. Un campo non personalizzato resta con i valori di default del codice.</summary>
[ApiController]
[Authorize]
[Route("api/layout")]
public class FormLayoutController(ApplicationDbContext db) : ControllerBase
{
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

    [Authorize(Policy = "AdminOnly")]
    [HttpPut("{screen}")]
    public async Task<ActionResult<LayoutScreenResponse>> Save(string screen, SaveLayoutRequest request, CancellationToken cancellationToken = default)
    {
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
                Required = item.FieldKey == "subject" || item.Required,
                UpdatedBy = user,
                UpdatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        var saved = await db.FormFieldSettings.AsNoTracking().Where(f => f.Screen == screen).ToListAsync(cancellationToken);
        return Ok(Merge(screen, saved));
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
                    s?.Required ?? def.Required,
                    def.CanHide,
                    def.Required);
            })
            .OrderBy(f => f.Order)
            .ToList();
        return new LayoutScreenResponse(screen, fields);
    }
}

public sealed record LayoutFieldResponse(
    string Key, string DefaultLabel, string Label, int Order, bool Visible, bool Required, bool CanHide, bool DefaultRequired);

public sealed record LayoutScreenResponse(string Screen, List<LayoutFieldResponse> Fields);

public sealed record SaveLayoutRequest(List<SaveLayoutFieldRequest>? Fields);

public sealed record SaveLayoutFieldRequest(string FieldKey, string? Label, int Order, bool Visible, bool Required);
