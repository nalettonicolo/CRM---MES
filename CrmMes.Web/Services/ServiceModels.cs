namespace CrmMes.Web.Services;

public sealed record InstalledMachine(Guid Id, Guid CustomerId, string CustomerName, Guid? WorkOrderId, string Name, string? Model,
    string SerialNumber, string? Location, DateTime? InstalledAt, DateTime? WarrantyUntil, string Status, string? Notes, int OpenRequestCount);

public sealed record ServiceRequestSummary(Guid Id, int Number, string Subject, string Priority, string Status, string Channel, DateTime OpenedAt, DateTime? ClosedAt);

public sealed record InstalledMachineDetail(Guid Id, Guid CustomerId, string CustomerName, Guid? WorkOrderId, string Name, string? Model,
    string SerialNumber, string? Location, DateTime? InstalledAt, DateTime? WarrantyUntil, string Status, string? Notes, List<ServiceRequestSummary> Requests);

public sealed record CreateInstalledMachineRequest(Guid CustomerId, Guid? WorkOrderId, string? Name, string? Model, string? SerialNumber,
    string? Location, DateTime? InstalledAt, DateTime? WarrantyUntil, string? Notes);

public sealed record ServiceRequestRow(Guid Id, int Number, Guid InstalledMachineId, string MachineName, string SerialNumber, Guid CustomerId,
    string CustomerName, string Subject, string? Description, string Priority, string Status, string Channel, string? RequestedBy, string? ContactInfo,
    DateTime OpenedAt, DateTime? ClosedAt, int InterventionCount);

public sealed record CreateServiceRequestRequest(Guid InstalledMachineId, string? Subject, string? Description, string? Priority, string? Channel,
    string? RequestedBy, string? ContactInfo);

public sealed record ServiceIntervention(Guid Id, string? TechnicianName, DateTime ScheduledAt, DateTime? CompletedAt, decimal? Hours,
    bool InWarranty, string? Description, string? MaterialsUsed, string? Notes);

public sealed record CreateServiceInterventionRequest(string? TechnicianName, DateTime? ScheduledAt, bool Completed, decimal? Hours, bool InWarranty,
    string? Description, string? MaterialsUsed, string? Notes);

public sealed record ServiceRequestDetail(Guid Id, int Number, Guid InstalledMachineId, string MachineName, string SerialNumber, Guid CustomerId,
    string CustomerName, string Subject, string? Description, string Priority, string Status, string Channel, string? RequestedBy, string? ContactInfo,
    DateTime OpenedAt, DateTime? ClosedAt, List<ServiceIntervention> Interventions);

public static class ServiceLabels
{
    public static string Status(string status) => status switch
    {
        "Open" => "Aperta",
        "InProgress" => "In lavorazione",
        "Closed" => "Chiusa",
        _ => status,
    };

    public static string StatusClass(string status) => status switch
    {
        "Open" => "warn",
        "InProgress" => "info",
        "Closed" => "ok",
        _ => "",
    };

    public static string Priority(string priority) => priority == "Urgente" ? "Urgente" : "Normale";

    public static string Channel(string channel) => channel switch
    {
        "Phone" => "Telefono",
        "Email" => "Email",
        "Portal" => "Portale",
        _ => channel,
    };

    public static bool UnderWarranty(DateTime? warrantyUntil) => warrantyUntil is { } until && until.Date >= DateTime.Today;
}
