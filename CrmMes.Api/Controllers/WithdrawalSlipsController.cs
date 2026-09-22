using CrmMes.Api.Services;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CrmMes.Api.Controllers;

[ApiController]
[Route("api/withdrawal-slips")]
public class WithdrawalSlipsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly WithdrawalItemBuilder _itemBuilder;

    public WithdrawalSlipsController(ApplicationDbContext dbContext, WithdrawalItemBuilder itemBuilder)
    {
        _dbContext = dbContext;
        _itemBuilder = itemBuilder;
    }

    [Authorize]
    [HttpGet]
    public async Task<ActionResult<IEnumerable<WithdrawalSlipSummaryResponse>>> GetWithdrawalSlips(
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.WithdrawalSlips.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(slip => slip.Status == status.Trim());
        }

        if (!HasElevatedAccess())
        {
            var userId = GetCurrentUserId();
            if (userId is null)
            {
                return Unauthorized();
            }

            query = query.Where(slip => slip.RequestedByUserId == userId ||
                slip.Area.Users.Any(user => user.Id == userId));
        }

        var slips = await query
            .OrderByDescending(slip => slip.CreatedAt)
            .Take(100)
            .Select(slip => new WithdrawalSlipSummaryResponse(
                slip.Id,
                slip.Code,
                slip.AreaId,
                slip.RequestedByUserId,
                slip.Status,
                slip.CreatedAt,
                slip.Items.Count,
                slip.Items.Count(item => item.IsMissing)))
            .ToListAsync(cancellationToken);

        return Ok(slips);
    }

    [Authorize]
    [HttpPost]
    public async Task<ActionResult<WithdrawalSlipResponse>> CreateWithdrawalSlip(
        CreateWithdrawalSlipRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.AreaId == Guid.Empty || request.RequestedByUserId == Guid.Empty)
        {
            return BadRequest(new { message = "AreaId e RequestedByUserId sono obbligatori." });
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return BadRequest(new { message = "La distinta deve contenere almeno un materiale." });
        }

        if (request.Items.Any(item => string.IsNullOrWhiteSpace(item.MaterialCode) || item.Quantity <= 0))
        {
            return BadRequest(new { message = "Ogni riga deve avere codice materiale e quantità maggiore di zero." });
        }

        var areaExists = await _dbContext.Areas
            .AnyAsync(area => area.Id == request.AreaId && area.IsActive, cancellationToken);
        var userExists = await _dbContext.Users
            .AnyAsync(user => user.Id == request.RequestedByUserId && user.IsActive, cancellationToken);

        if (!areaExists || !userExists)
        {
            return BadRequest(new { message = "Area o utente non trovato o non attivo." });
        }

        var currentUserId = GetCurrentUserId();
        if (!HasElevatedAccess() && (currentUserId is null || currentUserId != request.RequestedByUserId))
        {
            return Forbid();
        }

        if (!HasElevatedAccess() && !await _dbContext.Areas
                .AnyAsync(area => area.Id == request.AreaId && area.Users.Any(user => user.Id == currentUserId), cancellationToken))
        {
            return Forbid();
        }

        var slip = new WithdrawalSlip
        {
            // Appends a short random suffix: the timestamp alone is only second-precision, so two
            // slips created within the same second (a real possibility under normal usage) would
            // otherwise collide on the unique Code index.
            Code = string.IsNullOrWhiteSpace(request.Code)
                ? $"DP-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}"
                : request.Code.Trim(),
            AreaId = request.AreaId,
            RequestedByUserId = request.RequestedByUserId,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            WorkOrderId = request.WorkOrderId,
            Status = "Draft"
        };

        foreach (var item in await _itemBuilder.BuildAsync(slip.Id, request.Items, cancellationToken))
        {
            slip.Items.Add(item);
        }

        _dbContext.WithdrawalSlips.Add(slip);
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "WithdrawalSlipCreated",
            EntityType = "WithdrawalSlip",
            EntityId = slip.Id,
            UserName = GetCurrentUserName(),
            Details = $"Distinta {slip.Code} creata con {slip.Items.Count} righe."
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetWithdrawalSlip), new { id = slip.Id }, ToResponse(slip));
    }

    [Authorize]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WithdrawalSlipResponse>> EditWithdrawalSlip(
        Guid id,
        EditWithdrawalSlipRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return BadRequest(new { message = "La distinta deve contenere almeno un materiale." });
        }

        if (request.Items.Any(item => string.IsNullOrWhiteSpace(item.MaterialCode) || item.Quantity <= 0))
        {
            return BadRequest(new { message = "Ogni riga deve avere codice materiale e quantità maggiore di zero." });
        }

        var slip = await _dbContext.WithdrawalSlips
            .Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (slip is null)
        {
            return NotFound();
        }

        if (!HasElevatedAccess() && !await CanAccessSlipAsync(slip, cancellationToken))
        {
            return Forbid();
        }

        if (slip.Status != "Draft")
        {
            return Conflict(new { message = "Solo una distinta in bozza può essere modificata." });
        }

        // Only the still-open missing materials this slip generated are removed; ones already
        // linked to a purchase order (Ordered) or fulfilled (Resolved) are left as history.
        var openMissing = await _dbContext.MissingMaterials
            .Where(mm => mm.WithdrawalSlipId == id && mm.Status == "Open")
            .ToListAsync(cancellationToken);
        _dbContext.MissingMaterials.RemoveRange(openMissing);

        // WithdrawalSlipId is a required FK, so removing an item from this navigation collection is
        // enough: EF Core's change tracker schedules the delete on its own. Also calling
        // WithdrawalItems.Remove(item) on top of that double-tracks the deletion and throws a
        // DbUpdateConcurrencyException ("0 rows affected") because the row is already gone by the
        // time the second delete command runs.
        foreach (var item in slip.Items.ToList())
        {
            slip.Items.Remove(item);
        }

        foreach (var item in await _itemBuilder.BuildAsync(slip.Id, request.Items, cancellationToken))
        {
            slip.Items.Add(item);
        }

        slip.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "WithdrawalSlipUpdated",
            EntityType = "WithdrawalSlip",
            EntityId = slip.Id,
            UserName = GetCurrentUserName(),
            Details = $"Distinta {slip.Code} modificata, {slip.Items.Count} righe."
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(slip));
    }

    [Authorize]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WithdrawalSlipResponse>> GetWithdrawalSlip(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var slip = await _dbContext.WithdrawalSlips
            .AsNoTracking()
            .Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (slip is null)
        {
            return NotFound();
        }

        if (!HasElevatedAccess() && !await CanAccessSlipAsync(slip, cancellationToken))
        {
            return Forbid();
        }

        return Ok(ToResponse(slip));
    }

    [Authorize]
    [HttpPost("{id:guid}/ready")]
    public async Task<ActionResult<WithdrawalSlipResponse>> MarkReady(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var slip = await _dbContext.WithdrawalSlips
            .Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (slip is null)
        {
            return NotFound();
        }

        if (!HasElevatedAccess() && !await CanAccessSlipAsync(slip, cancellationToken))
        {
            return Forbid();
        }

        if (slip.Status != "Draft")
        {
            return Conflict(new { message = "Solo una distinta in bozza può diventare pronta." });
        }

        if (slip.Items.Any(item => item.IsMissing))
        {
            return Conflict(new { message = "La distinta contiene materiali mancanti.", status = "Draft" });
        }

        slip.Status = "Ready";
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "WithdrawalSlipReady",
            EntityType = "WithdrawalSlip",
            EntityId = slip.Id,
            UserName = GetCurrentUserName(),
            Details = $"Distinta {slip.Code} pronta per il prelievo."
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(slip));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost("{id:guid}/close")]
    public async Task<ActionResult<WithdrawalSlipResponse>> CloseWithdrawalSlip(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var slip = await _dbContext.WithdrawalSlips
            .Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (slip is null)
        {
            return NotFound();
        }

        if (!HasElevatedAccess() && !await CanAccessSlipAsync(slip, cancellationToken))
        {
            return Forbid();
        }

        if (slip.Status is "Closed" or "Cancelled")
        {
            return Conflict(new { message = "Solo una distinta in bozza o pronta può essere chiusa." });
        }

        var codes = slip.Items.Select(item => item.MaterialCode).Distinct().ToArray();
        var materials = await _dbContext.Materials
            .Where(material => codes.Contains(material.Code) && material.IsActive)
            .ToDictionaryAsync(material => material.Code, cancellationToken);

        var unavailable = slip.Items
            .Where(item => !materials.TryGetValue(item.MaterialCode, out var material) || material.Stock < item.Quantity)
            .Select(item => item.MaterialCode)
            .Distinct()
            .ToArray();

        if (unavailable.Length > 0)
        {
            return Conflict(new { message = "Stock insufficiente per uno o più materiali.", materials = unavailable });
        }

        foreach (var item in slip.Items)
        {
            materials[item.MaterialCode].Stock -= item.Quantity;
            item.IsMissing = false;
        }

        slip.Status = "Closed";
        await CreateLowStockAlertsAsync(materials.Values, cancellationToken);
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "WithdrawalSlipClosed",
            EntityType = "WithdrawalSlip",
            EntityId = slip.Id,
            UserName = GetCurrentUserName(),
            Details = $"Distinta {slip.Code} chiusa, scarico stock per {slip.Items.Count} righe."
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(ToResponse(slip));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> CancelWithdrawalSlip(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var slip = await _dbContext.WithdrawalSlips
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (slip is null)
        {
            return NotFound();
        }

        if (!HasElevatedAccess() && !await CanAccessSlipAsync(slip, cancellationToken))
        {
            return Forbid();
        }

        if (slip.Status == "Closed")
        {
            return Conflict(new { message = "Una distinta chiusa non può essere annullata." });
        }

        slip.Status = "Cancelled";
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "WithdrawalSlipCancelled",
            EntityType = "WithdrawalSlip",
            EntityId = slip.Id,
            UserName = GetCurrentUserName(),
            Details = $"Distinta {slip.Code} annullata."
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task CreateLowStockAlertsAsync(IEnumerable<Material> materials, CancellationToken cancellationToken)
    {
        var belowMinimum = materials
            .Where(material => material.MinStock > 0 && material.Stock < material.MinStock)
            .ToList();

        if (belowMinimum.Count == 0)
        {
            return;
        }

        var codes = belowMinimum.Select(material => material.Code).ToArray();
        var alreadyOpen = await _dbContext.MissingMaterials
            .Where(mm => mm.Status == "Open" && mm.Source == "MinStock" && codes.Contains(mm.MaterialCode))
            .Select(mm => mm.MaterialCode)
            .ToListAsync(cancellationToken);
        var alreadyOpenSet = alreadyOpen.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var material in belowMinimum.Where(material => !alreadyOpenSet.Contains(material.Code)))
        {
            _dbContext.MissingMaterials.Add(new MissingMaterial
            {
                MaterialCode = material.Code,
                Quantity = material.MinStock - material.Stock,
                Source = "MinStock",
                Status = "Open"
            });
        }
    }

    private string? GetCurrentUserName() => User.FindFirstValue(ClaimTypes.Name);

    private bool HasElevatedAccess() => User.IsInRole("Admin") || User.IsInRole("Warehouse");

    private Guid? GetCurrentUserId()
    {
        var value = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private async Task<bool> CanAccessSlipAsync(WithdrawalSlip slip, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        return userId is not null &&
            (slip.RequestedByUserId == userId || await _dbContext.Areas
                .AnyAsync(area => area.Id == slip.AreaId && area.Users.Any(user => user.Id == userId), cancellationToken));
    }

    private static WithdrawalSlipResponse ToResponse(WithdrawalSlip slip)
    {
        return new WithdrawalSlipResponse(
            slip.Id,
            slip.Code,
            slip.AreaId,
            slip.RequestedByUserId,
            slip.WorkOrderId,
            slip.Status,
            slip.Notes,
            slip.CreatedAt,
            slip.Items.Select(item => new WithdrawalSlipItemResponse(
                item.Id,
                item.MaterialCode,
                item.Description,
                item.Quantity,
                item.Unit,
                item.IsMissing)).ToList());
    }
}

public sealed record CreateWithdrawalSlipRequest(
    Guid AreaId,
    Guid RequestedByUserId,
    string? Code,
    string? Notes,
    List<CreateWithdrawalSlipItemRequest> Items,
    Guid? WorkOrderId = null);

public sealed record CreateWithdrawalSlipItemRequest(
    string MaterialCode,
    decimal Quantity,
    string? Description,
    string? Unit);

public sealed record EditWithdrawalSlipRequest(
    string? Notes,
    List<CreateWithdrawalSlipItemRequest> Items);

public sealed record WithdrawalSlipResponse(
    Guid Id,
    string Code,
    Guid AreaId,
    Guid RequestedByUserId,
    Guid? WorkOrderId,
    string Status,
    string? Notes,
    DateTime CreatedAt,
    IReadOnlyList<WithdrawalSlipItemResponse> Items);

public sealed record WithdrawalSlipSummaryResponse(
    Guid Id,
    string Code,
    Guid AreaId,
    Guid RequestedByUserId,
    string Status,
    DateTime CreatedAt,
    int ItemCount,
    int MissingItemCount);

public sealed record WithdrawalSlipItemResponse(
    Guid Id,
    string MaterialCode,
    string Description,
    decimal Quantity,
    string Unit,
    bool IsMissing);
