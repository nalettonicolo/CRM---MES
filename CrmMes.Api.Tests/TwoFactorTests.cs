using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using CrmMes.Api.Controllers;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Api.Tests;

/// <summary>Two-factor authentication: optional for everyone, compulsory for the roles the Admin picks.</summary>
public class TwoFactorTests : IClassFixture<AdminSeededApiTestFixture>
{
    private const string Password = "TestPass123!";
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _admin;

    public TwoFactorTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    // RFC 6238, appendix B (SHA-1 seed "12345678901234567890"), last six digits.
    [Theory]
    [InlineData(59, "287082")]
    [InlineData(1111111109, "081804")]
    [InlineData(1234567890, "005924")]
    public void Codes_MatchTheRfcTestVectors(long unixSeconds, string expected)
    {
        var secret = Encoding.ASCII.GetBytes("12345678901234567890");

        Assert.Equal(expected, Totp.Code(secret, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixSeconds))));
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Totp.Base32(secret));
    }

    [Fact]
    public void Verify_AcceptsDrift_RefusesReuseAndGarbage()
    {
        var secret = Totp.NewSecret();
        var now = DateTimeOffset.UtcNow;
        var step = Totp.StepAt(now);

        Assert.Equal(step - 1, Totp.Verify(secret, Totp.Code(secret, step - 1), now, 0));
        Assert.Null(Totp.Verify(secret, Totp.Code(secret, step), now, lastUsedStep: step));
        Assert.Null(Totp.Verify(secret, Totp.Code(secret, step + 2), now, 0));
        Assert.Null(Totp.Verify(secret, "12ab", now, 0));
    }

    [Fact]
    public void StoredSecret_IsEncrypted_AndTamperingIsDetected()
    {
        var protector = new SecretProtector("a-signing-key-used-only-by-this-test-0123456789");
        var secret = Totp.NewSecret();
        var stored = protector.Protect(secret);

        Assert.StartsWith("v1:", stored);
        Assert.DoesNotContain(Totp.Base32(secret), stored);
        Assert.Equal(secret, protector.Unprotect(stored));
        Assert.Null(new SecretProtector("another-key-another-key-another-key-0123").Unprotect(stored));
        Assert.Null(protector.Unprotect(stored[..^4] + "AAAA"));
    }

    [Fact]
    public async Task Login_AsksForTheCode_OnceTwoFactorIsActive()
    {
        var user = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Warehouse");
        var client = _fixture.Factory.AuthenticatedClient(user.Token);
        var (secret, recovery) = await EnableAsync(client);

        var first = await LoginAsync(user.Email);
        Assert.Equal(string.Empty, first.Token);
        Assert.False(string.IsNullOrEmpty(first.TwoFactorChallenge));

        // The challenge is not an access token.
        using var withChallenge = _fixture.Factory.CreateClient();
        withChallenge.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.TwoFactorChallenge);
        Assert.Equal(HttpStatusCode.Unauthorized, (await withChallenge.GetAsync("/api/materials")).StatusCode);

        var wrong = await _fixture.Client.PostAsJsonAsync("/api/auth/login/2fa", new TwoFactorLoginRequest(first.TwoFactorChallenge, "000000"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        var ok = await _fixture.Client.PostAsJsonAsync("/api/auth/login/2fa",
            new TwoFactorLoginRequest(first.TwoFactorChallenge, Totp.Code(secret, Totp.StepAt(DateTimeOffset.UtcNow) + 1)));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var tokens = await ok.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.False(string.IsNullOrEmpty(tokens!.Token));
        Assert.Equal(HttpStatusCode.OK, (await _fixture.Factory.AuthenticatedClient(tokens.Token).GetAsync("/api/materials")).StatusCode);

        // A recovery code works once.
        var again = await LoginAsync(user.Email);
        var withRecovery = await _fixture.Client.PostAsJsonAsync("/api/auth/login/2fa", new TwoFactorLoginRequest(again.TwoFactorChallenge, recovery[0].ToLowerInvariant()));
        Assert.Equal(HttpStatusCode.OK, withRecovery.StatusCode);
        var reuse = await LoginAsync(user.Email);
        var reused = await _fixture.Client.PostAsJsonAsync("/api/auth/login/2fa", new TwoFactorLoginRequest(reuse.TwoFactorChallenge, recovery[0]));
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);

        using var scope = _fixture.Factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.SingleAsync(u => u.Id == user.UserId);
        Assert.StartsWith("v1:", stored.TwoFactorSecret);
        Assert.DoesNotContain(recovery[1], stored.TwoFactorRecoveryCodes);
    }

    [Fact]
    public async Task RequiredRoles_OnlyAllowTheSetup_UntilItIsActive()
    {
        var sales = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Sales");
        await ConfigureCompanyAsync();
        (await _admin.PutAsJsonAsync("/api/company-profile/security", new SaveSecuritySettingsRequest(["Sales"]))).EnsureSuccessStatusCode();

        var login = await LoginAsync(sales.Email);
        Assert.True(login.TwoFactorSetupRequired);
        var limited = _fixture.Factory.AuthenticatedClient(login.Token);
        var blocked = await limited.GetAsync("/api/customers");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Contains("verifica in due passaggi", await blocked.Content.ReadAsStringAsync());

        await EnableAsync(limited);
        var renewed = await (await _fixture.Client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(login.RefreshToken)))
            .Content.ReadFromJsonAsync<AuthResponse>();
        Assert.False(renewed!.TwoFactorSetupRequired);
        var full = _fixture.Factory.AuthenticatedClient(renewed.Token);
        Assert.Equal(HttpStatusCode.OK, (await full.GetAsync("/api/customers")).StatusCode);

        var disable = await full.PostAsJsonAsync("/api/account/2fa/disable", new DisableTwoFactorRequest(Password, "123456"));
        Assert.Equal(HttpStatusCode.Conflict, disable.StatusCode);

        (await _admin.PutAsJsonAsync("/api/company-profile/security", new SaveSecuritySettingsRequest([]))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Admin_CantLockThemselvesOut_AndCanResetALostPhone()
    {
        await ConfigureCompanyAsync();
        var ownRole = await _admin.PutAsJsonAsync("/api/company-profile/security", new SaveSecuritySettingsRequest(["Admin"]));
        Assert.Equal(HttpStatusCode.Conflict, ownRole.StatusCode);

        var user = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Purchasing");
        await EnableAsync(_fixture.Factory.AuthenticatedClient(user.Token));
        Assert.False(string.IsNullOrEmpty((await LoginAsync(user.Email)).TwoFactorChallenge));

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.PostAsync($"/api/users/{user.UserId}/2fa/reset", null)).StatusCode);

        var after = await LoginAsync(user.Email);
        Assert.Null(after.TwoFactorChallenge);
        Assert.False(string.IsNullOrEmpty(after.Token));
        var refreshOld = await _fixture.Client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(user.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refreshOld.StatusCode);

        var notAdmin = await _fixture.Factory.AuthenticatedClient(after.Token).PostAsync($"/api/users/{_fixture.Admin.UserId}/2fa/reset", null);
        Assert.Equal(HttpStatusCode.Forbidden, notAdmin.StatusCode);
    }

    private async Task<(byte[] Secret, List<string> Recovery)> EnableAsync(HttpClient client)
    {
        var setup = await (await client.PostAsync("/api/account/2fa/setup", null)).Content.ReadFromJsonAsync<TwoFactorSetupResponse>();
        Assert.StartsWith("data:image/png;base64,", setup!.QrCodePng);
        var secret = FromBase32(setup.Secret.Replace(" ", string.Empty));
        Assert.Contains($"secret={setup.Secret.Replace(" ", string.Empty)}", setup.OtpAuthUri);

        var enabled = await client.PostAsJsonAsync("/api/account/2fa/enable",
            new TwoFactorCodeRequest(Totp.Code(secret, Totp.StepAt(DateTimeOffset.UtcNow))));
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        var codes = (await enabled.Content.ReadFromJsonAsync<RecoveryCodesResponse>())!.RecoveryCodes;
        Assert.Equal(10, codes.Count);
        return (secret, codes);
    }

    private async Task<AuthResponse> LoginAsync(string email)
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private async Task ConfigureCompanyAsync() =>
        (await _admin.PutAsJsonAsync("/api/company-profile", new SaveCompanyProfileRequest("Officine Sicure srl", null, null, null, null, "generic", null)))
            .EnsureSuccessStatusCode();

    private static byte[] FromBase32(string text)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in text)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. bytes];
    }
}
