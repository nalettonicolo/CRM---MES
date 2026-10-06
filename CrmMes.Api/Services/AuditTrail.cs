using System.Security.Claims;
using CrmMes.Core.Data;
using CrmMes.Core.Models;

namespace CrmMes.Api.Services;

public static class AuditTrail
{
    public static void Add(ApplicationDbContext db, ClaimsPrincipal user, string action, string entityType, Guid? entityId, string details)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            UserName = user.FindFirstValue(ClaimTypes.Name),
            Details = details
        });
    }
}
