namespace CrmMes.Web.Services;

// Shapes of the API responses the web platform reads (mirrors of the records in CrmMes.Api).

public sealed record AuthResponse(
    string Token, string RefreshToken, DateTime ExpiresAt, Guid UserId, string Name, string Email, string Role,
    string? TwoFactorChallenge = null, bool TwoFactorSetupRequired = false);

public sealed record TwoFactorStatus(bool Enabled, bool Required, int RecoveryCodesLeft);

public sealed record TwoFactorSetupInfo(string Secret, string OtpAuthUri, string QrCodePng);

public sealed record RecoveryCodeList(List<string> RecoveryCodes);

public sealed record SecuritySettings(List<string> TwoFactorRoles, List<string> KnownRoles);

public sealed record SupportInfo(
    string? Name, string? Email, string? Phone, string? Hours, string? RustDeskIdServer, string? RustDeskKey,
    string? RemoteToolUrl, string ServerVersion, string Hosting);

public sealed record DatabaseState(
    string Provider, bool CanConnect, int? AppliedMigrations, string? LastMigration, List<string> PendingMigrations,
    long? SizeBytes, int? ActiveUsers, int? OpenWorkOrders);

public sealed record ServerDiagnostics(
    string ServerVersion, string Hosting, string Environment, string MachineName, string OperatingSystem, string Runtime,
    DateTime ServerTimeUtc, DateTime StartedAtUtc, double UptimeHours, bool ConfigFileLoaded, string? LogsPath,
    double? FreeDiskGigabytes, double? FreeLogDiskGigabytes, DatabaseState Database);

public sealed record CompanyProfile(
    bool IsConfigured, string CompanyName, string? VatNumber, string? Address, string? Phone, string? Email,
    string Sector, List<string> EnabledModules, string? Gs1CompanyPrefix = null);

public sealed record AccessArea(string Key, string Name, string? Module);

public sealed record AccessChannels(
    List<string> Channels,
    Dictionary<string, List<string>> Roles,
    Dictionary<string, List<string>> Areas,
    List<AccessArea> KnownAreas,
    List<string> KnownRoles);

public sealed record SaveAccessChannels(
    List<string> Channels,
    Dictionary<string, List<string>> Roles,
    Dictionary<string, List<string>> Areas);

public sealed record WorkOrderSummary(
    Guid Id, string Code, string ProductLotNumber, Guid ProductId, string ProductCode, string ProductName,
    decimal Quantity, string Status, DateTime? DueDate, DateTime CreatedAt, int OperationCount, int CompletedOperationCount);

public sealed record WorkOrderLookup(
    Guid Id, string Code, string ProductLotNumber, string ProductName, decimal Quantity, string Status, DateTime? DueDate,
    string? CustomerName, string? ActiveOperation);

public sealed record WorkOrderOperation(
    Guid Id, int SequenceNumber, string Name, string? Description, string? WorkCenter, decimal EstimatedMinutes,
    string Status, DateTime? StartedAt, DateTime? CompletedAt, decimal? ActualMinutes, decimal? PerformanceRatio,
    DateTime? PlannedStartAt, DateTime? PlannedEndAt, string? StartedBy, string? CompletedBy);

public sealed record WorkOrderDetail(
    Guid Id, string Code, string ProductLotNumber, Guid ProductId, decimal Quantity, Guid? AreaId,
    string? CustomerReference, string Status, DateTime? DueDate, string? Notes, DateTime CreatedAt,
    DateTime? ReleasedAt, DateTime? CompletedAt, List<WorkOrderOperation> Operations,
    Guid? CustomerId = null, Guid? QuoteId = null);

public sealed record WorkOrderMaterialLot(
    Guid MaterialLotId, string MaterialCode, string LotNumber, decimal QuantityConsumed, Guid WithdrawalSlipId, string WithdrawalSlipCode);

public sealed record WorkOrderDashboard(
    int PeriodDays,
    Dictionary<string, int> WorkOrdersByStatus,
    int OperationsCompletedInPeriod,
    decimal? AveragePerformanceRatio,
    int WorkOrdersCompletedInPeriod,
    decimal? OnTimeCompletionRate,
    decimal TotalDowntimeMinutes,
    decimal? AvailabilityRatio,
    decimal TotalScrapQuantity,
    decimal? QualityRatio,
    decimal? OeeRatio);

public sealed record Material(
    Guid Id, string Code, string Name, string Unit, decimal Stock, decimal MinStock, bool IsActive, bool BelowMinimum);
