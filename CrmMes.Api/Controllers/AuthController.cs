using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
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
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

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

        // Bootstrap: il primo utente mai registrato diventa Admin, così un ambiente nuovo
        // non richiede accesso diretto al database per creare il primo amministratore.
        var isFirstUser = !await _dbContext.Users.AnyAsync(cancellationToken);

        var user = new User
        {
            Name = name,
            Email = email,
            Role = isFirstUser ? "Admin" : "Operator"
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(await CreateResponseAsync(user, cancellationToken));
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

        return Ok(await CreateResponseAsync(user, cancellationToken));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(
        RefreshRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Unauthorized(new { message = "Refresh token non valido." });
        }

        var hash = HashToken(request.RefreshToken);
        var existing = await _dbContext.RefreshTokens
            .Include(rt => rt.User)
            .SingleOrDefaultAsync(rt => rt.TokenHash == hash, cancellationToken);

        if (existing is null || !existing.IsActive || !existing.User.IsActive)
        {
            return Unauthorized(new { message = "Refresh token non valido o scaduto." });
        }

        existing.RevokedAt = DateTime.UtcNow;
        var response = await CreateResponseAsync(existing.User, cancellationToken, replaces: existing);
        return Ok(response);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        RefreshRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return NoContent();
        }

        var hash = HashToken(request.RefreshToken);
        var existing = await _dbContext.RefreshTokens.SingleOrDefaultAsync(rt => rt.TokenHash == hash, cancellationToken);
        if (existing is not null && existing.RevokedAt is null)
        {
            existing.RevokedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }

    private async Task<AuthResponse> CreateResponseAsync(User user, CancellationToken cancellationToken, RefreshToken? replaces = null)
    {
        var key = _configuration["Jwt:Key"]
            ?? Environment.GetEnvironmentVariable("CRM_MES_JWT_KEY")
            ?? "development-only-key-change-before-deploy-32chars";
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role)
        };
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTime.UtcNow.Add(AccessTokenLifetime);
        var token = new JwtSecurityToken(claims: claims, expires: expiresAt, signingCredentials: credentials);

        var rawRefreshToken = GenerateRefreshToken();
        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = HashToken(rawRefreshToken),
            ExpiresAt = DateTime.UtcNow.Add(RefreshTokenLifetime)
        };
        _dbContext.RefreshTokens.Add(refreshToken);

        if (replaces is not null)
        {
            replaces.ReplacedByTokenId = refreshToken.Id;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AuthResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            rawRefreshToken,
            expiresAt,
            user.Id,
            user.Name,
            user.Email,
            user.Role);
    }

    private static string GenerateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public sealed record RegisterRequest(string? Name, string? Email, string? Password);
public sealed record LoginRequest(string? Email, string? Password);
public sealed record RefreshRequest(string? RefreshToken);
public sealed record AuthResponse(
    string Token,
    string RefreshToken,
    DateTime ExpiresAt,
    Guid UserId,
    string Name,
    string Email,
    string Role);
