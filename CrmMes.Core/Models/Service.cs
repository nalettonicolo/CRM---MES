namespace CrmMes.Core.Models;

/// <summary>A machine or panel installed at a customer's site: the anagraphic the Service department works
/// from (matricola, where it is, when the warranty ends) once a work order has shipped. Optionally traced back
/// to the work order it came from, for the technical file and the as-built product; "Decommissioned" when the
/// customer dismantles it or replaces it, kept for history.</summary>
public class InstalledMachine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    /// <summary>The job it was built and shipped on, when known; also where the EU declaration and the
    /// technical file of a machine-built product live (see MachineTesting.cs).</summary>
    public Guid? WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Model { get; set; }

    /// <summary>Matricola: unique within the company, so a technician can scan or type it to find the machine.</summary>
    public string SerialNumber { get; set; } = string.Empty;
    public string? Location { get; set; }
    public DateTime? InstalledAt { get; set; }
    public DateTime? WarrantyUntil { get; set; }
    public string? Notes { get; set; }

    /// <summary>"Active" or "Decommissioned".</summary>
    public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ServiceRequest> Requests { get; set; } = new List<ServiceRequest>();
}

/// <summary>A customer's call for help on an installed machine: phone, email or the customer portal. Stays
/// "Open" until a technician is assigned ("InProgress"), then "Closed" once the intervention that resolves it
/// is completed. One request can have more than one intervention (a first visit that needs a follow-up).</summary>
public class ServiceRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>1, 2, 3... across the company ("RA 14").</summary>
    public int Number { get; set; }
    public Guid InstalledMachineId { get; set; }
    public InstalledMachine InstalledMachine { get; set; } = null!;

    public string Subject { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>"Normal" or "Urgente".</summary>
    public string Priority { get; set; } = "Normal";

    /// <summary>"Open", "InProgress" or "Closed".</summary>
    public string Status { get; set; } = "Open";

    /// <summary>"Phone", "Email" or "Portal" (how the customer reached out).</summary>
    public string Channel { get; set; } = "Phone";
    public string? RequestedBy { get; set; }
    public string? ContactInfo { get; set; }
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }

    public ICollection<ServiceIntervention> Interventions { get; set; } = new List<ServiceIntervention>();
}

/// <summary>One technician's visit or remote session against a service request: what was done, how long it
/// took, whether it is covered by warranty. Completing the last open intervention is a separate, explicit step
/// (the request may need a follow-up visit), never automatic.</summary>
public class ServiceIntervention
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServiceRequestId { get; set; }
    public ServiceRequest ServiceRequest { get; set; } = null!;

    public string? TechnicianName { get; set; }
    public DateTime ScheduledAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public decimal? Hours { get; set; }

    /// <summary>Whether this visit is covered by the machine's warranty (decided per visit: a request opened
    /// while under warranty can still need a billable follow-up after it expires).</summary>
    public bool InWarranty { get; set; }
    public string? Description { get; set; }
    public string? MaterialsUsed { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class ServiceRequestStatus
{
    public const string Open = "Open";
    public const string InProgress = "InProgress";
    public const string Closed = "Closed";
}
