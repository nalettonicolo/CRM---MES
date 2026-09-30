namespace CrmMes.Core.Models;

/// <summary>Link between a machine and the program: the machine (or the gateway reading it over OPC UA,
/// MQTT, Modbus...) authenticates with a token of its own, stored here only as a SHA-256 hash. One per
/// machine; regenerating it revokes the old one. This is the automated data exchange with the
/// management system that Industry 4.0 / Transizione 5.0 interconnection asks for.</summary>
public class MachineConnection
{
    public Guid EquipmentId { get; set; }
    public Equipment Equipment { get; set; } = null!;
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>First characters of the token, to recognise which one is configured on a gateway.</summary>
    public string TokenPrefix { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public string? LastState { get; set; }
    public long? LastPieceCounter { get; set; }
}

/// <summary>One reading sent by a machine: its state at that instant and, when it has one, the
/// cumulative piece counter and the active alarm. A state lasts until the next reading, so a machine
/// only needs to send on change plus a periodic heartbeat.</summary>
public class MachineEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EquipmentId { get; set; }
    public Equipment Equipment { get; set; } = null!;
    public DateTime Timestamp { get; set; }

    /// <summary>"Running", "Idle", "Setup", "Stopped", "Alarm" or "Off".</summary>
    public string State { get; set; } = "Running";
    public long? PieceCounter { get; set; }
    public long? ScrapCounter { get; set; }
    public string? AlarmCode { get; set; }
    public string? AlarmText { get; set; }
    public string? WorkOrderCode { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}
