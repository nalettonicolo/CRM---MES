using System.Security.Claims;

namespace CrmMes.Api.Services;

/// <summary>Who may see company cost data: work order costs and margins, and work center hourly rates.
/// Everyone else can still log hours and handle quotes and sale prices, but never sees what a job costs.
/// Enforced server-side (the "ViewMargins" policy and response filtering), not just hidden in the client.</summary>
public static class MarginAccess
{
    public static readonly string[] Roles = ["Admin", "Management"];

    public static bool CanView(ClaimsPrincipal user) => Roles.Any(user.IsInRole);
}
