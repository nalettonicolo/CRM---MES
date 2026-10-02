namespace CrmMes.Web.Services;



public sealed record SectorPackSummary(string Key, string Name, string Description, string Sector, bool Available);



public sealed record SectorPackLineIssue(int Row, string Message);



public sealed record EplanBomPreviewRow(int Row, string PartNumber, decimal Quantity, string Description);



public sealed record WireListPreviewRow(int Row, string From, string To, string Section, string Color);



public sealed record NutritionPreviewRow(int Row, string Nutrient, decimal Per100g);



public sealed record Dm3708PreviewRow(int Row, string PlantCode, string Address, string ClientName, decimal PowerKw, string Notes);



public sealed record SalPreviewRow(int Row, string WorkOrderCode, decimal PercentComplete, decimal Amount, string Notes);

public sealed record Cert31PreviewRow(int Row, string LotNumber, string MaterialCode, string CertificateNumber, string Issuer, DateTime IssuedOn);



public sealed record SectorPackPreview<T>(

    int RowCount, IReadOnlyList<T> Samples, IReadOnlyList<SectorPackLineIssue> Errors, int ErrorCount);



public sealed record SectorPackImportResult(

    int ImportedCount, int MaterialsCreated, int ErrorCount, IReadOnlyList<SectorPackLineIssue> Errors);



public sealed record SectorPackApplyResult(

    int AppliedCount, int SkippedCount, IReadOnlyList<SectorPackLineIssue> Issues, int ErrorCount);



public sealed record Dm3708GenerateRequest(string PlantCode, string Address, string ClientName, decimal PowerKw, string? Notes);



public sealed record ScaleReadingSubmitRequest(string MaterialCode, decimal WeightKg, string? Unit, DateTime? RecordedAt, string? Notes);



public sealed record ScaleReadingResult(Guid Id, string MaterialCode, decimal WeightKg, string Unit, DateTime RecordedAt, string? Notes);
