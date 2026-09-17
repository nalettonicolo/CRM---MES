using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public UsersController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
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

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new { message = "Nome ed email sono obbligatori." });
        }

        if (await _dbContext.Users.AnyAsync(user => user.Email == email, cancellationToken))
        {
            return Conflict(new { message = "Esiste già un utente con questa email." });
        }

        var user = new User
        {
            Name = name,
            Email = email,
            Role = "Operator"
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Created($"api/users/{user.Id}", new UserResponse(user.Id, user.Name, user.Email, user.Role, user.IsActive, user.CreatedAt));
    }
}

    public sealed record CreateUserRequest(string? Name, string? Email);
    public sealed record UserResponse(Guid Id, string Name, string Email, string Role, bool IsActive, DateTime CreatedAt);
