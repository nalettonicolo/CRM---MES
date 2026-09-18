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
}

public sealed record CreateUserRequest(string? Name, string? Email, string? Password, string? Role);
public sealed record UserResponse(Guid Id, string Name, string Email, string Role, bool IsActive, DateTime CreatedAt);
