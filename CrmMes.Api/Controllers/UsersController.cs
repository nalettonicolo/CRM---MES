using System.Security.Claims;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private static readonly HashSet<string> AllowedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Admin", "Warehouse", "Purchasing", "Operator"
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly IPasswordHasher<User> _passwordHasher;

    public UsersController(ApplicationDbContext dbContext, IPasswordHasher<User> passwordHasher)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<User>>> GetUsers(CancellationToken cancellationToken = default)
    {
        var users = await _dbContext.Users.AsNoTracking()
            .OrderBy(user => user.Name)
            .Select(user => new UserResponse(user.Id, user.Name, user.Email, user.Role, user.IsActive, user.CreatedAt))
            .ToListAsync(cancellationToken);
        return Ok(users);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost]
    public async Task<ActionResult<UserResponse>> CreateUser(
        CreateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim();
        var email = request.Email?.Trim().ToLowerInvariant();
        var role = string.IsNullOrWhiteSpace(request.Role) ? "Operator" : request.Role.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            return BadRequest(new { message = "Nome, email e password di almeno 8 caratteri sono obbligatori." });
        }

        if (!AllowedRoles.Contains(role))
        {
            return BadRequest(new { message = $"Ruolo non valido. Valori ammessi: {string.Join(", ", AllowedRoles)}." });
        }

        if (await _dbContext.Users.AnyAsync(user => user.Email == email, cancellationToken))
        {
            return Conflict(new { message = "Esiste già un utente con questa email." });
        }

        var user = new User
        {
            Name = name,
            Email = email,
            Role = role
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        _dbContext.Users.Add(user);
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "UserCreated",
            EntityType = "User",
            EntityId = user.Id,
            UserName = User.FindFirstValue(ClaimTypes.Name),
            Details = $"Utente {user.Email} creato con ruolo {user.Role}."
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Created($"api/users/{user.Id}", new UserResponse(user.Id, user.Name, user.Email, user.Role, user.IsActive, user.CreatedAt));
    }

    /// <summary>Sets or replaces the short PIN a user types at the shop-floor terminal to identify
    /// themselves — a separate, much shorter secret than their login password, meant for a kiosk, not for
    /// signing in. Admin-only, same as creating a user.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPut("{id:guid}/pin")]
    public async Task<IActionResult> SetUserPin(Guid id, SetUserPinRequest request, CancellationToken cancellationToken = default)
    {
        var pin = request.Pin?.Trim() ?? string.Empty;
        if (pin.Length < 4 || pin.Length > 8 || !pin.All(char.IsDigit))
        {
            return BadRequest(new { message = "Il PIN deve essere numerico, da 4 a 8 cifre." });
        }

        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        user.PinHash = _passwordHasher.HashPassword(user, pin);
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "UserPinSet",
            EntityType = "User",
            EntityId = user.Id,
            UserName = User.FindFirstValue(ClaimTypes.Name),
            Details = $"PIN terminale impostato per {user.Name}."
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>What the shop-floor terminal calls after an operator types their PIN: finds which active
    /// user it belongs to. Checked against every active user with a PIN set — fine for a small team, and
    /// avoids an indexable plaintext PIN lookup that would defeat hashing it in the first place.</summary>
    [HttpPost("identify-by-pin")]
    public async Task<ActionResult<IdentifyByPinResponse>> IdentifyByPin(IdentifyByPinRequest request, CancellationToken cancellationToken = default)
    {
        var pin = request.Pin?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(pin))
        {
            return BadRequest(new { message = "Indica il PIN." });
        }

        var candidates = await _dbContext.Users
            .Where(u => u.IsActive && u.PinHash != null)
            .ToListAsync(cancellationToken);

        var match = candidates.FirstOrDefault(u =>
            _passwordHasher.VerifyHashedPassword(u, u.PinHash!, pin) != PasswordVerificationResult.Failed);

        return match is null
            ? NotFound(new { message = "PIN non riconosciuto." })
            : Ok(new IdentifyByPinResponse(match.Id, match.Name));
    }
}

public sealed record CreateUserRequest(string? Name, string? Email, string? Password, string? Role);
public sealed record UserResponse(Guid Id, string Name, string Email, string Role, bool IsActive, DateTime CreatedAt);
public sealed record SetUserPinRequest(string? Pin);
public sealed record IdentifyByPinRequest(string? Pin);
public sealed record IdentifyByPinResponse(Guid Id, string Name);
