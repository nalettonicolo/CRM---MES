using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/capas")]
public class CapasController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public CapasController(ApplicationDbContext dbContext) => _dbContext = dbContext;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CapaResponse>>> GetCapas(
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.CorrectiveActions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(c => c.Status == status.Trim());
        }

        var items = await query
            .OrderByDescending(c => c.OpenedAt)
            .Select(c => new CapaResponse(
                c.Id, c.Code, c.NonConformityId, c.Title, c.Description, c.Status,
                c.OpenedAt, c.DueDate, c.ClosedAt, c.RootCause, c.CorrectiveActionText, c.PreventiveActionText))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CapaResponse>> GetCapa(Guid id, CancellationToken cancellationToken = default)
    {
        var capa = await _dbContext.CorrectiveActions.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (capa is null)
        {
            return NotFound();
        }

        return Ok(new CapaResponse(
            capa.Id, capa.Code, capa.NonConformityId, capa.Title, capa.Description, capa.Status,
            capa.OpenedAt, capa.DueDate, capa.ClosedAt, capa.RootCause, capa.CorrectiveActionText, capa.PreventiveActionText));
    }

    [Authorize(Policy = "Engineering")]
    [HttpPost]
    public async Task<ActionResult<CapaResponse>> CreateCapa(
        CreateCapaRequest request,
        CancellationToken cancellationToken = default)
    {
        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return BadRequest(new { message = "Il titolo è obbligatorio." });
        }

        if (request.NonConformityId.HasValue
            && !await _dbContext.NonConformities.AnyAsync(n => n.Id == request.NonConformityId, cancellationToken))
        {
            return BadRequest(new { message = "Non conformità collegata non trovata." });
        }

        var code = request.Code?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            code = await NextCapaCodeAsync(cancellationToken);
        }
        else if (await _dbContext.CorrectiveActions.AnyAsync(c => c.Code == code, cancellationToken))
        {
            return Conflict(new { message = "Esiste già una CAPA con questo codice." });
        }

        var capa = new CorrectiveAction
        {
            Code = code,
            NonConformityId = request.NonConformityId,
            Title = title,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            DueDate = request.DueDate,
            RootCause = string.IsNullOrWhiteSpace(request.RootCause) ? null : request.RootCause.Trim(),
            CorrectiveActionText = string.IsNullOrWhiteSpace(request.CorrectiveActionText) ? null : request.CorrectiveActionText.Trim(),
            PreventiveActionText = string.IsNullOrWhiteSpace(request.PreventiveActionText) ? null : request.PreventiveActionText.Trim(),
        };

        _dbContext.CorrectiveActions.Add(capa);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Created($"api/capas/{capa.Id}", new CapaResponse(
            capa.Id, capa.Code, capa.NonConformityId, capa.Title, capa.Description, capa.Status,
            capa.OpenedAt, capa.DueDate, capa.ClosedAt, capa.RootCause, capa.CorrectiveActionText, capa.PreventiveActionText));
    }

    [Authorize(Policy = "Engineering")]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CapaResponse>> UpdateCapa(
        Guid id,
        UpdateCapaRequest request,
        CancellationToken cancellationToken = default)
    {
        var capa = await _dbContext.CorrectiveActions.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (capa is null)
        {
            return NotFound();
        }

        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return BadRequest(new { message = "Il titolo è obbligatorio." });
        }

        capa.Title = title;
        capa.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        capa.DueDate = request.DueDate;
        capa.RootCause = string.IsNullOrWhiteSpace(request.RootCause) ? null : request.RootCause.Trim();
        capa.CorrectiveActionText = string.IsNullOrWhiteSpace(request.CorrectiveActionText) ? null : request.CorrectiveActionText.Trim();
        capa.PreventiveActionText = string.IsNullOrWhiteSpace(request.PreventiveActionText) ? null : request.PreventiveActionText.Trim();

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new CapaResponse(
            capa.Id, capa.Code, capa.NonConformityId, capa.Title, capa.Description, capa.Status,
            capa.OpenedAt, capa.DueDate, capa.ClosedAt, capa.RootCause, capa.CorrectiveActionText, capa.PreventiveActionText));
    }

    [Authorize(Policy = "Engineering")]
    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<CapaResponse>> UpdateCapaStatus(
        Guid id,
        UpdateCapaStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var status = request.Status?.Trim();
        if (status is not CapaStatuses.Open and not CapaStatuses.InProgress and not CapaStatuses.Closed)
        {
            return BadRequest(new { message = "Stato non valido." });
        }

        var capa = await _dbContext.CorrectiveActions.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (capa is null)
        {
            return NotFound();
        }

        capa.Status = status;
        capa.ClosedAt = status == CapaStatuses.Closed ? DateTime.UtcNow : null;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new CapaResponse(
            capa.Id, capa.Code, capa.NonConformityId, capa.Title, capa.Description, capa.Status,
            capa.OpenedAt, capa.DueDate, capa.ClosedAt, capa.RootCause, capa.CorrectiveActionText, capa.PreventiveActionText));
    }

    private async Task<string> NextCapaCodeAsync(CancellationToken cancellationToken)
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"CAPA-{year}-";
        var existing = await _dbContext.CorrectiveActions
            .Where(c => c.Code.StartsWith(prefix))
            .Select(c => c.Code)
            .ToListAsync(cancellationToken);
        var max = existing
            .Select(code => int.TryParse(code[prefix.Length..], out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
        return $"{prefix}{max + 1:D4}";
    }
}

public sealed record CapaResponse(
    Guid Id, string Code, Guid? NonConformityId, string Title, string? Description, string Status,
    DateTime OpenedAt, DateTime? DueDate, DateTime? ClosedAt, string? RootCause,
    string? CorrectiveActionText, string? PreventiveActionText);

public sealed record CreateCapaRequest(
    string? Code, Guid? NonConformityId, string? Title, string? Description, DateTime? DueDate,
    string? RootCause, string? CorrectiveActionText, string? PreventiveActionText);

public sealed record UpdateCapaRequest(
    string? Title, string? Description, DateTime? DueDate, string? RootCause,
    string? CorrectiveActionText, string? PreventiveActionText);

public sealed record UpdateCapaStatusRequest(string? Status);
