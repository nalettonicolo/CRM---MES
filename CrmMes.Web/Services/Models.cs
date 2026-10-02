namespace CrmMes.Web.Services;

// Shapes of the API responses the web platform reads (mirrors of the records in CrmMes.Api).

public sealed record AuthResponse(
    string Token, string RefreshToken, DateTime ExpiresAt, Guid UserId, string Name, string Email, string Role,
    string? TwoFactorChallenge = null, bool TwoFactorSetupRequired = false);

public sealed record TwoFactorStatus(bool Enabled, bool Required, int RecoveryCodesLeft);

public sealed record TwoFactorSetupInfo(string Secret, string OtpAuthUri, string QrCodePng);

public sealed record RecoveryCodeList(List<string> RecoveryCodes);

public sealed record SecuritySettings(List<string> TwoFactorRoles, List<string> KnownRoles);

public sealed record LicenseInfo(
    bool Enabled, string Status, string? Message, string? Plan, List<string>? Modules, int? MaxUsers,
    DateTime? ValidUntil, DateTime? CheckedAt, string? Customer);

public sealed record SupportTicket(Guid Id, int Number, string Subject, string Status, DateTime CreatedAt, string RequestedBy, string? Reply, DateTime? RepliedAt);

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
    Guid? CustomerId = null, Guid? QuoteId = null, string? ProductRevision = null,
    string? CustomerName = null, string? QuoteCode = null, decimal? SalePrice = null);

public sealed record WorkOrderMaterialLot(
    Guid MaterialLotId, string MaterialCode, string LotNumber, decimal QuantityConsumed, Guid WithdrawalSlipId, string WithdrawalSlipCode);

public sealed record WorkOrderWithdrawalSlip(Guid WithdrawalSlipId, string WithdrawalSlipCode);

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
    decimal? OeeRatio,
    string OeeSource = "Declared",
    decimal? MachineAvailabilityRatio = null,
    decimal? MachineQualityRatio = null,
    int MachinesReportingInPeriod = 0);

public sealed record SoftwareOriginDeclaration(
    string ProductName, string ProductVersion, string ProducerName, string? ProducerVat,
    decimal EuDevelopmentPercent, string DevelopmentPlaces, string? Signatory, DateTime? UpdatedAt,
    bool MeetsEuThreshold, string DeclarationText, DateTime IssuedOn);

public sealed record UiThemePreset(
    string Key, string Name, string Description,
    string Background, string Surface, string SurfaceRaised, string Ink, string Muted, string Line,
    string Accent, string AccentHover, string AccentSoft, string OnAccent, string Sidebar, string SidebarText,
    string Ok, string Warn, int Radius, string Density, string BackgroundStyle, int FieldBorder, int FieldHeight);

public sealed record UiThemeDto(
    string Preset, string Background, string Surface, string SurfaceRaised, string Ink, string Muted, string Line,
    string Accent, string AccentHover, string AccentSoft, string OnAccent, string Sidebar, string SidebarText,
    string Ok, string Warn, int Radius, string Density, string BackgroundStyle, int FieldBorder, int FieldHeight,
    Dictionary<string, string> CssVariables, List<UiThemePreset> Presets);

public sealed record Material(
    Guid Id, string Code, string Name, string Unit, decimal Stock, decimal MinStock, bool IsActive, bool BelowMinimum,
    decimal? ListPrice = null, decimal? VatRate = null);

public sealed record ArticleImportIssue(int Row, string? Code, string Message);

public sealed record ArticleImportSample(int Row, string Code, string Name, string Unit, decimal? Price, decimal? VatRate, string Outcome);

public sealed record ArticleImportResult(
    bool Preview, int Rows, int Created, int Updated, int Unchanged, int Skipped,
    List<ArticleImportIssue> Errors, List<ArticleImportIssue> Warnings, int ErrorCount, int WarningCount,
    Dictionary<string, string> Columns, List<ArticleImportSample> Samples);
