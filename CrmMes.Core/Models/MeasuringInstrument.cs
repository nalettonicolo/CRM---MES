namespace CrmMes.Core.Models;

public class MeasuringInstrument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? SerialNumber { get; set; }
    public DateTime? NextCalibrationDue { get; set; }
    public DateTime? LastCalibrationAt { get; set; }
    public int CalibrationIntervalDays { get; set; } = 365;
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public ICollection<InstrumentCalibration> Calibrations { get; set; } = [];
}

public static class CalibrationResults
{
    public const string Pass = "Pass";
    public const string Fail = "Fail";
}

public class InstrumentCalibration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstrumentId { get; set; }
    public MeasuringInstrument Instrument { get; set; } = null!;
    public DateTime CalibratedAt { get; set; } = DateTime.UtcNow;
    public DateTime NextDue { get; set; }
    public string Result { get; set; } = CalibrationResults.Pass;
    public string? CertificateNumber { get; set; }
    public string? Notes { get; set; }
    public string? PerformedBy { get; set; }
}

public static class CapaStatuses
{
    public const string Open = "Open";
    public const string InProgress = "InProgress";
    public const string Closed = "Closed";
}

public class CorrectiveAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public Guid? NonConformityId { get; set; }
    public NonConformity? NonConformity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = CapaStatuses.Open;
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DueDate { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? RootCause { get; set; }
    public string? CorrectiveActionText { get; set; }
    public string? PreventiveActionText { get; set; }
}

public static class AttendancePunchKinds
{
    public const string In = "In";
    public const string Out = "Out";
}

public class AttendancePunch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime PunchedAt { get; set; } = DateTime.UtcNow;
    public string Kind { get; set; } = AttendancePunchKinds.In;
    public string? Notes { get; set; }
}
