using CrmMes.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmMes.Api.Controllers;

/// <summary>The subscription as this installation knows it, for the clients' banner (in grace, suspended)
/// and the Admin's "check now".</summary>
[ApiController]
[Authorize]
[Route("api/license")]
public class LicenseController : ControllerBase
{
    private readonly LicenseState _state;
    private readonly LicenseHeartbeatService _heartbeat;

    public LicenseController(LicenseState state, LicenseHeartbeatService heartbeat)
    {
        _state = state;
        _heartbeat = heartbeat;
    }

    [HttpGet]
    public async Task<ActionResult<LicenseResponse>> Get(CancellationToken cancellationToken = default) =>
        Ok(ToResponse(await _state.CurrentAsync(cancellationToken)));

    [Authorize(Policy = "AdminOnly")]
    [HttpPost("check")]
    public async Task<ActionResult<LicenseResponse>> CheckNow(CancellationToken cancellationToken = default)
    {
        if (!_state.Enabled)
        {
            return Ok(ToResponse(await _state.CurrentAsync(cancellationToken)));
        }

        var ok = await _heartbeat.CheckNowAsync(cancellationToken);
        var snapshot = await _state.CurrentAsync(cancellationToken);
        return ok ? Ok(ToResponse(snapshot)) : StatusCode(StatusCodes.Status502BadGateway, new { message = "Console licenze non raggiungibile: riprova più tardi." });
    }

    private static LicenseResponse ToResponse(LicenseState.Snapshot s) =>
        new(s.Enabled, s.Status, s.Message, s.Plan, s.Modules?.ToList(), s.MaxUsers, s.ValidUntil, s.CheckedAt, s.Customer);
}

public sealed record LicenseResponse(
    bool Enabled, string Status, string? Message, string? Plan, List<string>? Modules, int? MaxUsers,
    DateTime? ValidUntil, DateTime? CheckedAt, string? Customer);
