using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace CrmMes.Api.Services;

/// <summary>The middle step of a login with two-factor: after the right password the API hands out a short
/// challenge (5 minutes) instead of the tokens, and exchanges it for them only with a valid code. The
/// challenge is signed with a key of its own, derived from the server key: it is not an access token, so
/// the API refuses it everywhere else.</summary>
public sealed class TwoFactorChallenges
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private const string Audience = "nicolomes-2fa-challenge";
    private readonly SymmetricSecurityKey _key;

    public TwoFactorChallenges(string signingKey)
    {
        _key = new SymmetricSecurityKey(HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(signingKey), 32,
            info: Encoding.UTF8.GetBytes("nicolomes-2fa-challenge-v1")));
    }

    public string Issue(Guid userId, string channel)
    {
        var token = new JwtSecurityToken(
            audience: Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim("channel", channel),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ],
            expires: DateTime.UtcNow.Add(Lifetime),
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (Guid UserId, string Channel)? Read(string? challenge)
    {
        if (string.IsNullOrWhiteSpace(challenge))
        {
            return null;
        }

        try
        {
            var principal = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(challenge, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _key,
                ValidateIssuer = false,
                ValidateAudience = true,
                ValidAudience = Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
            }, out _);
            return Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)
                ? (id, principal.FindFirstValue("channel") ?? AccessChannels.Desktop)
                : null;
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException)
        {
            return null;
        }
    }
}

public static class TwoFactorRules
{
    /// <summary>Claim carried by the tokens of a user whose role requires two-factor but who hasn't set it
    /// up yet: with it the API answers only the login and two-factor setup endpoints.</summary>
    public const string SetupClaim = "mfa_setup";

    public static IReadOnlyList<string> ParseRoles(string? stored) =>
        (stored ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(AccessChannels.Roles.Contains).Distinct().ToList();

    public static bool IsAllowedDuringSetup(PathString path) =>
        path.StartsWithSegments("/api/auth") || path.StartsWithSegments("/api/account/2fa") || path.StartsWithSegments("/api/support/info");
}
