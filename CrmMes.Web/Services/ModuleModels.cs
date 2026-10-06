namespace CrmMes.Web.Services;

public sealed record QualityCheckpoint(
    Guid Id, Guid ProductId, string Name, string? Unit, decimal? NominalValue, decimal? LowerLimit, decimal? UpperLimit, bool IsActive,
    string? ProductCode = null, string? ProductName = null);

public sealed record QualityNonConformity(
    Guid Id, Guid WorkOrderId, string WorkOrderCode, string OperationName, string Description,
    decimal ScrapQuantity, string? Notes, DateTime DetectedAt, string? ReportedBy, string? UnitSerialNumber);

public sealed record PlanningCategory(Guid Id, string Code, string Name, string ColorHex, int SequenceNumber, bool IsActive);

public sealed record PlanningProject(Guid Id, string Name, string Status, string? Notes, int SequenceNumber, bool IsActive, Guid? WorkOrderId);

public sealed record PlanningCell(Guid ProjectId, Guid CategoryId, DateTime WeekStart);

public sealed record HaccpControlPoint(
    Guid Id, string Name, string? Location, string? Hazard, string? Unit, decimal? MinValue, decimal? MaxValue,
    string? Frequency, string? CorrectiveActionHint, bool IsActive, bool IsNumeric,
    DateTime? LastReadAt, decimal? LastValue, bool? LastCompliant);

public sealed record HaccpReading(
    Guid Id, Guid ControlPointId, string ControlPointName, string? Location, string? Unit, decimal? MinValue,
    decimal? MaxValue, decimal? Value, bool Compliant, string? CorrectiveAction, string? Notes, DateTime ReadAt,
    string? OperatorName);

public sealed record HaccpTemplate(string Key, string Name, string Description, List<HaccpTemplatePoint> Points);

public sealed record HaccpTemplatePoint(
    string Name, string Location, string Hazard, string? Unit, decimal? MinValue, decimal? MaxValue,
    string Frequency, string CorrectiveActionHint);

public sealed record ApplyHaccpTemplateResult(string Key, int Created, int Skipped);

public sealed record SiteReportSummary(
    Guid Id, string Code, Guid WorkOrderId, string WorkOrderCode, string? CustomerName, string Status, DateTime WorkDate,
    decimal TotalMinutes, string? SignedByName, DateTime? SignedAt, string? CreatedBy);

public sealed record SiteReportHours(string TechnicianName, Guid? WorkCenterId, string? WorkCenterName, decimal Minutes);

public sealed record SiteReportMaterial(string? MaterialCode, string Description, decimal Quantity, string Unit);

public sealed record SiteReportDetail(
    Guid Id, string Code, Guid WorkOrderId, string WorkOrderCode, string ProductName, string? CustomerName, string Status,
    DateTime WorkDate, string? SiteAddress, string Description, string? Notes, string? SignedByName, string? SignatureImage,
    DateTime? SignedAt, string? CreatedBy, DateTime CreatedAt, List<SiteReportHours> Hours, List<SiteReportMaterial> Materials);

public sealed record SubcontractingOpenLine(
    Guid DocumentId, string DocumentCode, Guid LineId, Guid? SupplierId, string SupplierName, DateTime? SentAt,
    DateTime? ExpectedReturnAt, bool IsOverdue, string? Code, string Description, string Unit, decimal SentQuantity,
    decimal ReturnedQuantity, decimal ScrapQuantity, decimal OutstandingQuantity);

public sealed record ShipmentSummary(
    Guid Id, string Code, string Direction, string Status, string CarrierName, string? TrackingNumber,
    string? CounterpartReference, DateTime? ExpectedAt, DateTime? ShippedAt, DateTime? DeliveredAt, DateTime CreatedAt);

public sealed record ShipmentDetail(
    Guid Id, string Code, string Direction, string Status, Guid CarrierId, string CarrierName, string? TrackingNumber,
    Guid? PurchaseOrderId, string? PurchaseOrderCode, Guid? WorkOrderId, string? WorkOrderCode,
    string? CounterpartReference, string? Address, string? Notes, DateTime? ExpectedAt, DateTime? ShippedAt,
    DateTime? DeliveredAt, DateTime CreatedAt);

public sealed record MarginRow(
    Guid WorkOrderId, string WorkOrderCode, string ProductName, string? CustomerName, string Status,
    decimal? SalePrice, decimal EstimatedCost, decimal ActualCost,
    decimal? EstimatedMarginRatio, decimal? ActualMargin, decimal? ActualMarginRatio, int WarningCount);

public sealed record MarginOverview(
    int WorkOrderCount, int PricedWorkOrderCount, decimal Revenue, decimal ActualCost, decimal Margin,
    decimal? MarginRatio, int LossMakingCount, List<MarginRow> WorkOrders);

public sealed record WorkOrderCosting(
    Guid WorkOrderId, string WorkOrderCode, string ProductCode, string ProductName, decimal Quantity, string Status,
    decimal? SalePrice, CostBreakdown Estimated, CostBreakdown Actual,
    MarginSlice? EstimatedMargin, MarginSlice? ActualMargin, decimal ActualMinutes,
    List<MaterialCostLineView> Materials, List<LaborCostLineView> Labor, List<string> Warnings);

public sealed record CostBreakdown(decimal Material, decimal Labor)
{
    public decimal Total => Material + Labor;
}

public sealed record MarginSlice(decimal Amount, decimal? Ratio);

public sealed record MaterialCostLineView(string MaterialCode, decimal Quantity, decimal Cost, string PriceSource, decimal UnpricedQuantity);

public sealed record LaborCostLineView(
    string Kind, string Description, string? WorkCenter, string? Operator, decimal Minutes,
    decimal? HourlyRate, decimal? Cost, bool InProgress, Guid? LaborEntryId);

public sealed record SiteRow(Guid Id, string Name, string Code, string? Address, bool IsActive);

public sealed record AreaRow(Guid Id, string Name, string Code, bool IsActive, Guid? SiteId = null, string? DepartmentType = null);

public sealed record WorkCenterRow(Guid Id, string Code, string Name, string? Description, decimal DailyCapacityMinutes, bool IsActive, Guid? SiteId = null, decimal? HourlyRate = null, Guid? AreaId = null);

public sealed record UserRow(Guid Id, string Name, string Email, string Role, bool IsActive, DateTime CreatedAt);

public sealed record PanelCheck(string Clause, string Description, string? Result, string? Notes);

public sealed record PanelVerification(
    bool IsSaved, string Status, Guid WorkOrderId, string WorkOrderCode, string ProductCode, string ProductName,
    string? ProductLotNumber, string? CustomerName,
    string Standard, string? OriginalManufacturer, string? SystemReference, string? SerialNumber,
    decimal? RatedVoltage, decimal? RatedCurrent, decimal? RatedFrequency, decimal? ShortTimeWithstandCurrent,
    decimal? ConditionalShortCircuitCurrent, string? IpRating, string? InternalSeparation, string? EarthingSystem,
    decimal? InsulationResistanceMOhm, decimal? DielectricTestVoltage, string? Notes,
    DateTime? CompletedAt, string? VerifiedBy, List<PanelCheck> Checks);

public sealed record FoodLabelIngredientView(string MaterialCode, string Name, decimal QuantityPerUnit, List<string> Allergens, List<string> AllergenNames);

public sealed record FoodLabel(
    Guid WorkOrderId, string WorkOrderCode, string ProductCode, string SalesName, string? LotNumber, decimal Quantity,
    DateTime ProductionDate, DateTime? ExpiryDate, bool UseByDate, string? StorageConditions, string? NetQuantity,
    string? ProducerName, string? ProducerAddress,
    List<FoodLabelIngredientView> Ingredients, List<string> Allergens, List<string> Warnings);

public sealed record LogisticUnit(
    Guid Id, string Sscc, Guid? WorkOrderId, Guid? TransportDocumentId, string? ProductCode, string? ProductName,
    string? LotNumber, decimal? Quantity, DateTime? BestBefore, DateTime CreatedAt);

public sealed record WorkCenterCapacityPlanRow(
    string WorkCenterCode, string WorkCenterName, DateOnly Day,
    decimal CapacityMinutes, decimal CommittedMinutes, decimal FreeMinutes);

public sealed record MeasuringInstrumentRow(
    Guid Id, string Code, string Name, string? SerialNumber, DateTime? NextCalibrationDue,
    DateTime? LastCalibrationAt, int CalibrationIntervalDays, bool IsActive, string? Notes);

public sealed record CapaRow(
    Guid Id, string Code, Guid? NonConformityId, string Title, string? Description, string Status,
    DateTime OpenedAt, DateTime? DueDate, DateTime? ClosedAt, string? RootCause,
    string? CorrectiveActionText, string? PreventiveActionText);

public sealed record AttendancePunchRow(
    Guid Id, Guid UserId, string UserName, DateTime PunchedAt, string Kind, string? Notes);
