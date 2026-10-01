namespace CrmMes.Web.Services;

public sealed record Equipment(Guid Id, string Name, string Code, Guid? WorkCenterId, string? WorkCenterName, bool IsActive);

public sealed record MaintenanceTask(
    Guid Id, Guid EquipmentId, string EquipmentName, string Title, string? Description, string Type, string Status,
    DateTime? DueDate, DateTime? CompletedAt, int? RecurrenceDays, string? Notes);

public sealed record CreateMaintenanceTaskRequest(Guid EquipmentId, string? Title, string? Description, string? Type, DateTime? DueDate, int? RecurrenceDays);

public static class MaintenanceLabels
{
    public static string Type(string type) => type switch
    {
        "Preventiva" => "Preventiva",
        "Correttiva" => "Correttiva",
        _ => type,
    };

    public static string Status(string status) => status switch
    {
        "Pending" => "Da fare",
        "Completed" => "Completato",
        _ => status,
    };

    public static string StatusClass(string status) => status switch
    {
        "Pending" => "warn",
        "Completed" => "ok",
        _ => "",
    };

    /// <summary>A preventive task is overdue when its due date has passed without being completed.</summary>
    public static bool IsOverdue(MaintenanceTask task) => task.Status == "Pending" && task.DueDate is { } due && due.Date < DateTime.Today;
}
