using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Services;

/// <summary>"Il mio reparto": the jobs of the departments a user belongs to. A job is a department's when it
/// is assigned to it or still has a phase to do on one of its work centers. A user in no department is
/// not narrowed at all (they see everything, as before departments existed).</summary>
public static class DepartmentFilter
{
    public static Guid? CurrentUserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static async Task<IQueryable<WorkOrder>> ApplyAsync(
        IQueryable<WorkOrder> query, ApplicationDbContext db, Guid? userId, CancellationToken cancellationToken)
    {
        if (userId is null)
        {
            return query;
        }

        var areaIds = await db.Users.Where(u => u.Id == userId).SelectMany(u => u.Areas.Where(a => a.IsActive).Select(a => a.Id))
            .ToListAsync(cancellationToken);
        if (areaIds.Count == 0)
        {
            return query;
        }

        var workCenterNames = await db.WorkCenters.Where(w => w.AreaId != null && areaIds.Contains(w.AreaId.Value))
            .Select(w => w.Name).ToListAsync(cancellationToken);
        return query.Where(order => (order.AreaId != null && areaIds.Contains(order.AreaId.Value))
            || order.Operations.Any(op => op.Status != "Completed" && op.WorkCenter != null && workCenterNames.Contains(op.WorkCenter)));
    }
}
