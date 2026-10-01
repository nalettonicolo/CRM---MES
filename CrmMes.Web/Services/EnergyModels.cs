namespace CrmMes.Web.Services;

public sealed record EnergyConsumption(decimal? TotalKwh, int ReadingCount, DateTime From, DateTime To);

public sealed record DailyEnergy(DateTime Day, decimal Kwh);

public sealed record CreateEnergyProjectRequest(Guid EquipmentId, string? Title, string? Description, DateTime BaselineFrom, DateTime BaselineTo);

public sealed record SetAfterPeriodRequest(DateTime? AfterFrom, DateTime? AfterTo);

public sealed record WorkOrderEnergyByEquipment(Guid EquipmentId, string EquipmentName, decimal Kwh);

public sealed record WorkOrderEnergy(string WorkOrderCode, decimal? TotalKwh, List<WorkOrderEnergyByEquipment> ByEquipment);

public sealed record EnergyProject(
    Guid Id, Guid EquipmentId, string EquipmentName, string EquipmentCode, string Title, string? Description,
    DateTime BaselineFrom, DateTime BaselineTo, decimal? BaselineKwh, int BaselineReadingCount,
    DateTime? AfterFrom, DateTime? AfterTo, decimal? AfterKwh, int? AfterReadingCount, decimal? SavingsPercent,
    string? Notes, string? CreatedBy, DateTime CreatedAt);
