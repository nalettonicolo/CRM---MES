using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace CrmMes.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IPasswordHasher<User> _passwordHasher;

    public AuthController(
        ApplicationDbContext dbContext,
        IConfiguration configuration,
        IPasswordHasher<User> passwordHasher)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _passwordHasher = passwordHasher;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email?.Trim().ToLowerInvariant();
        var name = request.Name?.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            return BadRequest(new { message = "Nome, email e password di almeno 8 caratteri sono obbligatori." });
        }

        if (await _dbContext.Users.AnyAsync(user => user.Email == email, cancellationToken))
        {
            return Conflict(new { message = "Email già registrata." });
        }

        var user = new User
        {
            Name = name,
            Email = email,
            Role = "Operator"
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(CreateResponse(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email?.Trim().ToLowerInvariant();
        var user = await _dbContext.Users.SingleOrDefaultAsync(item => item.Email == email && item.IsActive, cancellationToken);

        if (user is null || string.IsNullOrWhiteSpace(user.PasswordHash) ||
            _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? string.Empty) == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new { message = "Credenziali non valide." });
        }

        return Ok(CreateResponse(user));
    }

    private AuthResponse CreateResponse(User user)
    {
        var key = _configuration["Jwt:Key"]
            ?? Environment.GetEnvironmentVariable("CRM_MES_JWT_KEY")
            ?? "development-only-key-change-before-deploy-32chars";
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role)
        };
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddHours(8), signingCredentials: credentials);
        return new AuthResponse(new JwtSecurityTokenHandler().WriteToken(token), user.Id, user.Name, user.Email, user.Role);
    }
}

public sealed record RegisterRequest(string? Name, string? Email, string? Password);
public sealed record LoginRequest(string? Email, string? Password);
public sealed record AuthResponse(string Token, Guid UserId, string Name, string Email, string Role);