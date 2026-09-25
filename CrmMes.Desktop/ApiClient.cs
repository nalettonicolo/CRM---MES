using System.Net.Http;
using System.Net.Http.Json;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.Json;

namespace CrmMes.Desktop;

public sealed class ApiClient
{
    private HttpClient _httpClient = new();

    public Uri BaseAddress => _httpClient.BaseAddress!;

    public ApiClient()
    {
        SetBaseUrl(ClientSettings.DefaultApiBaseUrl);
    }

    /// <summary>Ripunta il client a un nuovo indirizzo API (es. dopo un cambio nelle impostazioni).
    /// HttpClient vieta di modificare BaseAddress dopo la prima richiesta inviata, quindi non lo
    /// riusiamo: ne creiamo uno nuovo, portando avanti l'eventuale token di autenticazione già impostato.</summary>
    public void SetBaseUrl(string baseUrl)
    {
        var newClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/")
        };
        if (_httpClient.DefaultRequestHeaders.Authorization is { } authorization)
        {
            newClient.DefaultRequestHeaders.Authorization = authorization;
        }

        var previousClient = _httpClient;
        _httpClient = newClient;
        previousClient.Dispose();
    }

    public async Task<bool> EnsureLocalApiAsync(CancellationToken cancellationToken = default)
    {
        if (await TryHealthAsync(cancellationToken))
        {
            return true;
        }

        if (!_httpClient.BaseAddress!.IsLoopback)
        {
            // Indirizzo remoto configurato esplicitamente: non ha senso tentare di avviare un'API locale.
            return false;
        }

        var apiPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "CrmMes.Api", "bin", "Debug", "net8.0", "CrmMes.Api.exe"));
        if (!File.Exists(apiPath))
        {
            return false;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = apiPath,
            WorkingDirectory = Path.GetDirectoryName(apiPath),
            UseShellExecute = false,
            CreateNoWindow = true,
            Environment =
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["ASPNETCORE_URLS"] = "http://localhost:5092"
            }
        });

        for (var attempt = 0; attempt < 60; attempt++)
        {
            await Task.Delay(250, cancellationToken);
            if (await TryHealthAsync(cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> TryHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await IsHealthyAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public async Task<AuthDto> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "api/auth/login",
                new { email, password },
                cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new InvalidOperationException("Credenziali non valide.");
            }

            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<AuthDto>(cancellationToken: cancellationToken);
            return result ?? throw new InvalidOperationException("Risposta login non valida.");
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException("API non disponibile. Avvia CrmMes.Api sulla porta 5092.", exception);
        }
    }

    public async Task<AuthDto> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "api/auth/refresh",
                new { refreshToken },
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new InvalidOperationException("Sessione scaduta, effettua di nuovo il login.");
            }

            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<AuthDto>(cancellationToken: cancellationToken);
            return result ?? throw new InvalidOperationException("Risposta di rinnovo sessione non valida.");
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException("API non disponibile. Avvia CrmMes.Api sulla porta 5092.", exception);
        }
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync("api/auth/logout", new { refreshToken }, cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Best-effort: se l'API non è raggiungibile il refresh token scadrà comunque da solo.
        }
    }

    public void SetToken(string token)
    {
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<IReadOnlyList<MaterialDto>> SearchMaterialsAsync(string query, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<List<MaterialDto>>(
            $"api/materials?q={Uri.EscapeDataString(query)}",
            cancellationToken);
        return response ?? [];
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("health", cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public Task<IReadOnlyList<WithdrawalSlipSummaryDto>> GetWithdrawalSlipsAsync(CancellationToken cancellationToken = default)
        => GetAsync<WithdrawalSlipSummaryDto>("api/withdrawal-slips", cancellationToken);

    public async Task<WithdrawalSlipDetailDto> GetWithdrawalSlipAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/withdrawal-slips/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<WithdrawalSlipDetailDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta distinta non valida.");
    }

    public Task<IReadOnlyList<MissingMaterialDto>> GetMissingMaterialsAsync(string status = "Open", CancellationToken cancellationToken = default)
        => GetAsync<MissingMaterialDto>($"api/procurement/missing?status={Uri.EscapeDataString(status)}", cancellationToken);

    public Task<IReadOnlyList<LowStockMaterialDto>> GetLowStockAsync(CancellationToken cancellationToken = default)
        => GetAsync<LowStockMaterialDto>("api/procurement/low-stock", cancellationToken);

    public async Task<LowStockScanDto> ScanLowStockAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync("api/procurement/low-stock/scan", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<LowStockScanDto>(cancellationToken: cancellationToken)
            ?? new LowStockScanDto(0, 0, []);
    }

    public Task<IReadOnlyList<PurchaseOrderSummaryDto>> GetPurchaseOrdersAsync(CancellationToken cancellationToken = default)
        => GetAsync<PurchaseOrderSummaryDto>("api/procurement/purchase-orders", cancellationToken);

    public async Task<PurchaseOrderDetailDto> GetPurchaseOrderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/procurement/purchase-orders/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<PurchaseOrderDetailDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta ordine non valida.");
    }

    public async Task ConfirmPurchaseOrderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"api/procurement/purchase-orders/{id}/confirm", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task CancelPurchaseOrderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"api/procurement/purchase-orders/{id}/cancel", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task SetPurchaseOrderExpectedDeliveryAsync(Guid id, DateTime? expectedDeliveryDate, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync(
            $"api/procurement/purchase-orders/{id}/expected-delivery", new { expectedDeliveryDate }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>The planning board: every dated commitment (commesse, ordini fornitore, spedizioni,
    /// manutenzioni) in one aggregated, filterable list plus a summary dashboard. See PlanningController.</summary>
    public async Task<PlanningDto> GetPlanningAsync(
        DateTime? from = null, int weeks = 4, Guid? siteId = null, IReadOnlyList<string>? types = null, CancellationToken cancellationToken = default)
    {
        var parameters = new List<string> { $"weeks={weeks}" };
        if (from.HasValue)
        {
            parameters.Add($"from={from.Value:yyyy-MM-dd}");
        }

        if (siteId.HasValue)
        {
            parameters.Add($"siteId={siteId}");
        }

        if (types is { Count: > 0 })
        {
            parameters.Add($"types={Uri.EscapeDataString(string.Join(",", types))}");
        }

        using var response = await _httpClient.GetAsync($"api/planning?{string.Join("&", parameters)}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<PlanningDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta planning non valida.");
    }

    public async Task ReceiveRemainingQuantityAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await GetPurchaseOrderAsync(id, cancellationToken);
        var items = order.Items
            .Where(item => item.ReceivedQuantity < item.Quantity)
            .Select(item => new { purchaseOrderItemId = item.Id, quantity = item.Quantity - item.ReceivedQuantity })
            .ToList();

        if (items.Count == 0)
        {
            throw new InvalidOperationException("Non ci sono quantità residue da ricevere per questo ordine.");
        }

        using var response = await _httpClient.PostAsJsonAsync(
            $"api/procurement/purchase-orders/{id}/receive",
            new { items },
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<AreaDto>> GetAreasAsync(CancellationToken cancellationToken = default)
        => GetAsync<AreaDto>("api/areas", cancellationToken);

    public Task<IReadOnlyList<SiteDto>> GetSitesAsync(bool activeOnly = true, CancellationToken cancellationToken = default)
        => GetAsync<SiteDto>($"api/sites?activeOnly={activeOnly}", cancellationToken);

    public async Task<SiteDetailDto> GetSiteDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/sites/{id}/detail", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<SiteDetailDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta dettaglio sede non valida.");
    }

    public async Task CreateSiteAsync(string name, string code, string? address, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/sites", new { name, code, address }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeactivateSiteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync($"api/sites/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<EquipmentDto>> GetEquipmentAsync(bool activeOnly = true, CancellationToken cancellationToken = default)
        => GetAsync<EquipmentDto>($"api/equipment?activeOnly={activeOnly}", cancellationToken);

    public async Task<EquipmentDetailDto> GetEquipmentDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/equipment/{id}/detail", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<EquipmentDetailDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta dettaglio macchina non valida.");
    }

    public async Task CreateEquipmentAsync(string name, string code, Guid? workCenterId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/equipment", new { name, code, workCenterId }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeactivateEquipmentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync($"api/equipment/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<MaintenanceTaskDto>> GetMaintenanceTasksAsync(string? status = null, string? type = null, Guid? equipmentId = null, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");
        if (!string.IsNullOrWhiteSpace(type)) query.Add($"type={Uri.EscapeDataString(type)}");
        if (equipmentId.HasValue) query.Add($"equipmentId={equipmentId}");
        var queryString = query.Count > 0 ? $"?{string.Join('&', query)}" : string.Empty;
        return GetAsync<MaintenanceTaskDto>($"api/maintenance-tasks{queryString}", cancellationToken);
    }

    public async Task CreateMaintenanceTaskAsync(Guid equipmentId, string title, string? description, string type, DateTime? dueDate, int? recurrenceDays, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/maintenance-tasks", new { equipmentId, title, description, type, dueDate, recurrenceDays }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task CompleteMaintenanceTaskAsync(Guid id, Guid? completedByUserId, string? notes, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"api/maintenance-tasks/{id}/complete", new { completedByUserId, notes }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<QualityCheckpointDto>> GetQualityCheckpointsAsync(Guid productId, CancellationToken cancellationToken = default)
        => GetAsync<QualityCheckpointDto>($"api/quality-checkpoints?productId={productId}", cancellationToken);

    public async Task CreateQualityCheckpointAsync(Guid productId, string name, string? unit, decimal? nominalValue, decimal? lowerLimit, decimal? upperLimit, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/quality-checkpoints", new { productId, name, unit, nominalValue, lowerLimit, upperLimit }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeactivateQualityCheckpointAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync($"api/quality-checkpoints/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<QualityMeasurementDto>> GetQualityMeasurementsAsync(Guid workOrderId, CancellationToken cancellationToken = default)
        => GetAsync<QualityMeasurementDto>($"api/quality-measurements?workOrderId={workOrderId}", cancellationToken);

    public async Task RecordQualityMeasurementAsync(Guid checkpointId, Guid workOrderId, Guid? workOrderUnitId, decimal measuredValue, Guid? measuredByUserId, string? notes, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/quality-measurements", new { checkpointId, workOrderId, workOrderUnitId, measuredValue, measuredByUserId, notes }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<QualityCertificateDto> GetQualityCertificateAsync(Guid workOrderId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/quality-measurements/certificate/{workOrderId}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<QualityCertificateDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta certificato non valida.");
    }

    public async Task<AreaDetailDto> GetAreaDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/areas/{id}/detail", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<AreaDetailDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta dettaglio area non valida.");
    }

    public async Task AssignUserToAreaAsync(Guid areaId, Guid userId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"api/areas/{areaId}/users/{userId}", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task UnassignUserFromAreaAsync(Guid areaId, Guid userId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync($"api/areas/{areaId}/users/{userId}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<UserRowDto>> GetUsersAsync(CancellationToken cancellationToken = default)
        => GetAsync<UserRowDto>("api/users", cancellationToken);

    public async Task<UserDetailDto> GetUserDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/users/{id}/detail", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<UserDetailDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta dettaglio utente non valida.");
    }

    public Task<IReadOnlyList<SupplierDto>> GetSuppliersAsync(CancellationToken cancellationToken = default)
        => GetAsync<SupplierDto>("api/suppliers", cancellationToken);

    public async Task<SupplierDetailDto> GetSupplierDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/suppliers/{id}/detail", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<SupplierDetailDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta dettaglio fornitore non valida.");
    }

    public async Task EditSupplierAsync(Guid id, string name, string? email, string? phone, string? website, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync($"api/suppliers/{id}", new { name, email, phone, website }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<CatalogSearchResultDto>> SearchCatalogAsync(string query, CancellationToken cancellationToken = default)
        => GetAsync<CatalogSearchResultDto>($"api/supplier-catalog/search?q={Uri.EscapeDataString(query)}", cancellationToken);

    public Task<IReadOnlyList<CarrierDto>> GetCarriersAsync(bool activeOnly = true, CancellationToken cancellationToken = default)
        => GetAsync<CarrierDto>($"api/carriers?activeOnly={activeOnly}", cancellationToken);

    public async Task<CarrierDetailDto> GetCarrierDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/carriers/{id}/detail", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CarrierDetailDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta dettaglio corriere non valida.");
    }

    public async Task<WorkCenterDetailDto> GetWorkCenterDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/work-centers/{id}/detail", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<WorkCenterDetailDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta dettaglio centro di lavoro non valida.");
    }

    public async Task CreateCarrierAsync(string name, string code, string? email, string? phone, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/carriers", new { name, code, email, phone }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeactivateCarrierAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync($"api/carriers/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<ShipmentSummaryDto>> GetShipmentsAsync(string? direction = null, string? status = null, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(direction))
        {
            query.Add($"direction={Uri.EscapeDataString(direction)}");
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query.Add($"status={Uri.EscapeDataString(status)}");
        }

        var queryString = query.Count > 0 ? $"?{string.Join('&', query)}" : string.Empty;
        return GetAsync<ShipmentSummaryDto>($"api/shipments{queryString}", cancellationToken);
    }

    public async Task<ShipmentDetailDto> CreateShipmentAsync(
        string direction,
        Guid carrierId,
        string? trackingNumber,
        Guid? purchaseOrderId,
        Guid? workOrderId,
        string? counterpartReference,
        string? address,
        string? notes,
        DateTime? expectedAt,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/shipments",
            new { direction, carrierId, trackingNumber, purchaseOrderId, workOrderId, counterpartReference, address, notes, expectedAt },
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ShipmentDetailDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta spedizione non valida.");
    }

    public async Task<ShipmentDetailDto> ShipShipmentAsync(Guid id, string? trackingNumber, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync($"api/shipments/{id}/ship", new { trackingNumber }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ShipmentDetailDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta spedizione non valida.");
    }

    public async Task DeliverShipmentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"api/shipments/{id}/deliver", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task CancelShipmentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"api/shipments/{id}/cancel", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<ImportSummaryDto> ImportCatalogExcelAsync(string filePath, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        await using var stream = File.OpenRead(filePath);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", Path.GetFileName(filePath));

        using var response = await _httpClient.PostAsync("api/supplier-catalog/import-excel", content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ImportSummaryDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta di importazione non valida.");
    }

    /// <summary>Best-effort: works only for a PDF whose catalog is a simple text table with a
    /// recognizable header row — see the server-side XML doc on the endpoint for what it can't handle
    /// (scanned PDFs, complex graphic layouts).</summary>
    public async Task<ImportSummaryDto> ImportCatalogPdfAsync(string filePath, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        await using var stream = File.OpenRead(filePath);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", Path.GetFileName(filePath));

        using var response = await _httpClient.PostAsync("api/supplier-catalog/import-pdf", content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ImportSummaryDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta di importazione non valida.");
    }

    public async Task CreateWithdrawalSlipAsync(
        Guid areaId,
        Guid requestedByUserId,
        string? notes,
        IReadOnlyList<(string MaterialCode, decimal Quantity)> items,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            areaId,
            requestedByUserId,
            notes,
            items = items.Select(item => new { materialCode = item.MaterialCode, quantity = item.Quantity })
        };
        using var response = await _httpClient.PostAsJsonAsync("api/withdrawal-slips", payload, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task EditWithdrawalSlipAsync(
        Guid id,
        string? notes,
        IReadOnlyList<(string MaterialCode, decimal Quantity)> items,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            notes,
            items = items.Select(item => new { materialCode = item.MaterialCode, quantity = item.Quantity })
        };
        using var response = await _httpClient.PutAsJsonAsync($"api/withdrawal-slips/{id}", payload, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task MarkWithdrawalSlipReadyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"api/withdrawal-slips/{id}/ready", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task CloseWithdrawalSlipAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"api/withdrawal-slips/{id}/close", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task CancelWithdrawalSlipAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"api/withdrawal-slips/{id}/cancel", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task CreatePurchaseOrderAsync(
        Guid supplierId,
        IReadOnlyList<(string MaterialCode, decimal Quantity, decimal UnitPrice)> items,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            supplierId,
            items = items.Select(item => new { materialCode = item.MaterialCode, quantity = item.Quantity, unitPrice = item.UnitPrice })
        };
        using var response = await _httpClient.PostAsJsonAsync("api/procurement/purchase-orders", payload, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task EditPurchaseOrderAsync(
        Guid id,
        Guid supplierId,
        IReadOnlyList<(string MaterialCode, decimal Quantity, decimal UnitPrice)> items,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            supplierId,
            items = items.Select(item => new { materialCode = item.MaterialCode, quantity = item.Quantity, unitPrice = item.UnitPrice })
        };
        using var response = await _httpClient.PutAsJsonAsync($"api/procurement/purchase-orders/{id}", payload, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task CreateMaterialAsync(string code, string name, string unit, decimal stock, decimal minStock, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/materials",
            new { code, name, unit, stock, minStock },
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task CreateSupplierAsync(string name, string code, string? email, string? phone, string? website = null, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/suppliers",
            new { name, code, email, phone, website },
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<WorkCenterDto>> GetWorkCentersAsync(Guid? siteId = null, CancellationToken cancellationToken = default)
        => GetAsync<WorkCenterDto>(siteId.HasValue ? $"api/work-centers?siteId={siteId}" : "api/work-centers", cancellationToken);

    public Task<IReadOnlyList<WorkCenterLoadDto>> GetWorkCenterLoadAsync(Guid? siteId = null, CancellationToken cancellationToken = default)
        => GetAsync<WorkCenterLoadDto>(siteId.HasValue ? $"api/work-centers/load?siteId={siteId}" : "api/work-centers/load", cancellationToken);

    public async Task CreateWorkCenterAsync(string code, string name, decimal dailyCapacityMinutes, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/work-centers",
            new { code, name, description = (string?)null, dailyCapacityMinutes },
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeactivateWorkCenterAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync($"api/work-centers/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task CreateAreaAsync(string name, string code, Guid? siteId = null, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/areas", new { name, code, siteId }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task CreateUserAsync(string name, string email, string password, string role, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/users", new { name, email, password, role }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<ProductSummaryDto>> GetProductsAsync(bool activeOnly = true, string? q = null, CancellationToken cancellationToken = default)
    {
        var query = $"api/products?activeOnly={activeOnly}";
        if (!string.IsNullOrWhiteSpace(q))
        {
            query += $"&q={Uri.EscapeDataString(q)}";
        }

        return GetAsync<ProductSummaryDto>(query, cancellationToken);
    }

    public async Task<ProductDetailDto> GetProductAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/products/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ProductDetailDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta prodotto non valida.");
    }

    public async Task CreateProductAsync(string code, string name, string? description, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/products", new { code, name, description }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task EditProductAsync(Guid id, string name, string? description, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync($"api/products/{id}", new { name, description }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeactivateProductAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync($"api/products/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task ImportBillOfMaterialAsync(Guid productId, string filePath, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        await using var stream = File.OpenRead(filePath);
        using var fileContent = new StreamContent(stream);
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            extension == ".csv" ? "text/csv" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", Path.GetFileName(filePath));

        using var response = await _httpClient.PostAsync($"api/products/{productId}/bom/import", content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task ReplaceBillOfMaterialAsync(
        Guid productId,
        IReadOnlyList<(string MaterialCode, decimal Quantity, string? Notes)> items,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            items = items.Select(item => new { materialCode = item.MaterialCode, quantity = item.Quantity, notes = item.Notes })
        };
        using var response = await _httpClient.PutAsJsonAsync($"api/products/{productId}/bom", payload, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task ReplaceRoutingAsync(
        Guid productId,
        IReadOnlyList<(string Name, string? Description, string? WorkCenter, decimal EstimatedMinutes)> steps,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            steps = steps.Select(step => new { name = step.Name, description = step.Description, workCenter = step.WorkCenter, estimatedMinutes = step.EstimatedMinutes })
        };
        using var response = await _httpClient.PutAsJsonAsync($"api/products/{productId}/routing", payload, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<WorkOrderSummaryDto>> GetWorkOrdersAsync(string? status = null, Guid? siteId = null, CancellationToken cancellationToken = default)
    {
        var parameters = new List<string>();
        if (!string.IsNullOrWhiteSpace(status))
        {
            parameters.Add($"status={Uri.EscapeDataString(status)}");
        }

        if (siteId.HasValue)
        {
            parameters.Add($"siteId={siteId}");
        }

        var query = "api/work-orders" + (parameters.Count > 0 ? "?" + string.Join("&", parameters) : "");
        return GetAsync<WorkOrderSummaryDto>(query, cancellationToken);
    }

    public async Task<WorkOrderDetailDto> GetWorkOrderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/work-orders/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<WorkOrderDetailDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta commessa non valida.");
    }

    /// <summary>Looks a work order up by its printed code — what the shop-floor terminal calls after
    /// reading a barcode/QR label, since the operator scans a printed code, not a GUID.</summary>
    public async Task<WorkOrderDetailDto> GetWorkOrderByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/work-orders/by-code/{Uri.EscapeDataString(code)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"Nessuna commessa trovata con il codice \"{code}\".");
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<WorkOrderDetailDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta commessa non valida.");
    }

    public async Task CreateWorkOrderAsync(
        Guid productId, decimal quantity, Guid? areaId, string? customerReference, DateTime? dueDate, string? notes,
        string? productLotNumber = null, CancellationToken cancellationToken = default)
    {
        var payload = new { productId, quantity, code = (string?)null, areaId, customerReference, dueDate, notes, productLotNumber };
        using var response = await _httpClient.PostAsJsonAsync("api/work-orders", payload, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task EditWorkOrderAsync(
        Guid id, decimal quantity, Guid? areaId, string? customerReference, DateTime? dueDate, string? notes,
        CancellationToken cancellationToken = default)
    {
        var payload = new { quantity, areaId, customerReference, dueDate, notes };
        using var response = await _httpClient.PutAsJsonAsync($"api/work-orders/{id}", payload, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>Releases a work order. If material availability is short and <paramref name="force"/>
    /// is false, throws <see cref="MaterialShortfallException"/> instead of the generic error so the
    /// caller can show the shortfall and offer to retry with force=true.</summary>
    public async Task ReleaseWorkOrderAsync(Guid id, bool force = false, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"api/work-orders/{id}/release?force={force}", null, cancellationToken);
        if (!force && response.StatusCode == HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var availability = TryExtractAvailability(body);
            if (availability is not null)
            {
                throw new MaterialShortfallException(availability);
            }
        }

        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>Runs the day-granularity finite-capacity scheduler for this work order's still-open
    /// operations. See the server-side XML doc on the endpoint for what "day-granularity" and "finite
    /// capacity" mean here — this is not a drag-and-drop Gantt, just enough to know which day(s) a phase
    /// should land on given each work center's registered capacity.</summary>
    public async Task ScheduleWorkOrderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"api/work-orders/{id}/schedule", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<MaterialAvailabilityDto> CheckMaterialAvailabilityAsync(Guid workOrderId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/work-orders/{workOrderId}/material-check", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<MaterialAvailabilityDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta di verifica materiali non valida.");
    }

    public Task<IReadOnlyList<WorkOrderMaterialLotDto>> GetWorkOrderMaterialLotsAsync(Guid workOrderId, CancellationToken cancellationToken = default)
        => GetAsync<WorkOrderMaterialLotDto>($"api/work-orders/{workOrderId}/material-lots", cancellationToken);

    public async Task<WorkOrderDashboardDto> GetWorkOrderDashboardAsync(int days = 7, Guid? siteId = null, CancellationToken cancellationToken = default)
    {
        var query = $"api/work-orders/dashboard?days={days}";
        if (siteId.HasValue)
        {
            query += $"&siteId={siteId}";
        }

        using var response = await _httpClient.GetAsync(query, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<WorkOrderDashboardDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta cruscotto non valida.");
    }

    public Task<IReadOnlyList<MaterialLotSummaryDto>> GetMaterialLotsAsync(string? materialCode = null, bool onlyWithStock = false, CancellationToken cancellationToken = default)
    {
        var query = $"api/material-lots?onlyWithStock={onlyWithStock}";
        if (!string.IsNullOrWhiteSpace(materialCode))
        {
            query += $"&materialCode={Uri.EscapeDataString(materialCode)}";
        }

        return GetAsync<MaterialLotSummaryDto>(query, cancellationToken);
    }

    public async Task<MaterialLotDetailDto> GetMaterialLotAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/material-lots/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<MaterialLotDetailDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta lotto non valida.");
    }

    public async Task CreateMaterialLotAsync(string materialCode, string lotNumber, decimal quantity, string? notes, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/material-lots", new { materialCode, lotNumber, quantity, notes }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static MaterialAvailabilityDto? TryExtractAvailability(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("availability", out var availabilityElement))
            {
                return null;
            }

            return availabilityElement.Deserialize<MaterialAvailabilityDto>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task CancelWorkOrderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"api/work-orders/{id}/cancel", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task CompleteWorkOrderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"api/work-orders/{id}/complete", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task StartOperationAsync(Guid workOrderId, Guid operationId, string? operatorName = null, Guid? operatorId = null, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync(
            $"api/work-orders/{workOrderId}/operations/{operationId}/start{OperatorQuery(operatorName, operatorId)}", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task CompleteOperationAsync(Guid workOrderId, Guid operationId, string? operatorName = null, Guid? operatorId = null, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync(
            $"api/work-orders/{workOrderId}/operations/{operationId}/complete{OperatorQuery(operatorName, operatorId)}", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<OperationDowntimeDto>> GetOperationDowntimesAsync(Guid workOrderId, Guid operationId, CancellationToken cancellationToken = default)
        => GetAsync<OperationDowntimeDto>($"api/work-orders/{workOrderId}/operations/{operationId}/downtimes", cancellationToken);

    public async Task<OperationDowntimeDto> StartDowntimeAsync(Guid workOrderId, Guid operationId, string reason, string? notes, string? operatorName = null, Guid? operatorId = null, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"api/work-orders/{workOrderId}/operations/{operationId}/downtime/start{OperatorQuery(operatorName, operatorId)}", new { reason, notes }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<OperationDowntimeDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta fermo non valida.");
    }

    /// <summary>Query string for the optional operator-attribution parameters every terminal-triggered
    /// action forwards to the API, so it can log who (as identified by PIN) did what — empty when no
    /// operator is known, e.g. an action from the office client. operatorId is the authoritative link
    /// (User.Id); operatorName is kept as a display snapshot alongside it.</summary>
    private static string OperatorQuery(string? operatorName, Guid? operatorId = null)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(operatorName))
        {
            parts.Add($"operatorName={Uri.EscapeDataString(operatorName)}");
        }

        if (operatorId is Guid id)
        {
            parts.Add($"operatorId={id}");
        }

        return parts.Count == 0 ? string.Empty : $"?{string.Join('&', parts)}";
    }

    public async Task EndDowntimeAsync(Guid workOrderId, Guid operationId, Guid downtimeId, string? operatorName = null, Guid? operatorId = null, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync(
            $"api/work-orders/{workOrderId}/operations/{operationId}/downtime/{downtimeId}/end{OperatorQuery(operatorName, operatorId)}", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<NonConformityDto>> GetNonConformitiesAsync(Guid workOrderId, Guid operationId, CancellationToken cancellationToken = default)
        => GetAsync<NonConformityDto>($"api/work-orders/{workOrderId}/operations/{operationId}/non-conformities", cancellationToken);

    public async Task<NonConformityDto> RegisterNonConformityAsync(Guid workOrderId, Guid operationId, string description, decimal scrapQuantity, string? notes, string? operatorName = null, Guid? workOrderUnitId = null, Guid? operatorId = null, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"api/work-orders/{workOrderId}/operations/{operationId}/non-conformities{OperatorQuery(operatorName, operatorId)}",
            new { description, scrapQuantity, notes, workOrderUnitId }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<NonConformityDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta non conformità non valida.");
    }

    /// <summary>Per-serial traceability: which units a work order produced and what happened to each.
    /// Empty when the work order's quantity wasn't a whole number at creation (see WorkOrderUnit).</summary>
    public Task<IReadOnlyList<WorkOrderUnitDto>> GetWorkOrderUnitsAsync(Guid workOrderId, CancellationToken cancellationToken = default)
        => GetAsync<WorkOrderUnitDto>($"api/work-orders/{workOrderId}/units", cancellationToken);

    /// <summary>Full per-serial traceability for one unit: which phases it went through and when, and
    /// which material lots fed it. See WorkOrderUnitDetailResponse on the API side.</summary>
    public async Task<WorkOrderUnitDetailDto> GetWorkOrderUnitDetailAsync(Guid workOrderId, Guid unitId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/work-orders/{workOrderId}/units/{unitId}/detail", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<WorkOrderUnitDetailDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta dettaglio unità non valida.");
    }

    public async Task<IdentifyOperatorDto> IdentifyOperatorByPinAsync(string pin, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/users/identify-by-pin", new { pin }, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("PIN non riconosciuto.");
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<IdentifyOperatorDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta identificazione non valida.");
    }

    public async Task SetUserPinAsync(Guid userId, string pin, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync($"api/users/{userId}/pin", new { pin }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<WorkOrderWithdrawalSlipDto> GenerateWithdrawalSlipAsync(Guid workOrderId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"api/work-orders/{workOrderId}/generate-withdrawal-slip", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<WorkOrderWithdrawalSlipDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta di generazione distinta non valida.");
    }

    private async Task<IReadOnlyList<T>> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<List<T>>(cancellationToken: cancellationToken);
        return result ?? [];
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("Il tuo ruolo utente non ha i permessi per questa operazione.");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Errore API ({(int)response.StatusCode}): {TryExtractMessage(body) ?? body}");
    }

    private static string? TryExtractMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record MaterialDto(
    Guid Id,
    string Code,
    string Name,
    string Unit,
    decimal Stock,
    decimal MinStock,
    bool IsActive,
    bool BelowMinimum);

public sealed record AuthDto(
    string Token,
    string RefreshToken,
    DateTime ExpiresAt,
    Guid UserId,
    string Name,
    string Email,
    string Role);

public sealed record WithdrawalSlipSummaryDto(
    Guid Id,
    string Code,
    Guid AreaId,
    Guid RequestedByUserId,
    string Status,
    DateTime CreatedAt,
    int ItemCount,
    int MissingItemCount);

public sealed record WithdrawalSlipDetailDto(
    Guid Id,
    string Code,
    Guid AreaId,
    Guid RequestedByUserId,
    string Status,
    string? Notes,
    DateTime CreatedAt,
    List<WithdrawalSlipItemDto> Items);

public sealed record WithdrawalSlipItemDto(
    Guid Id,
    string MaterialCode,
    string Description,
    decimal Quantity,
    string Unit,
    bool IsMissing);

public sealed record MissingMaterialDto(
    Guid Id,
    Guid? WithdrawalSlipId,
    string MaterialCode,
    decimal Quantity,
    string Source,
    string Status,
    DateTime CreatedAt);

public sealed record LowStockMaterialDto(
    Guid Id,
    string Code,
    string Name,
    string Unit,
    decimal Stock,
    decimal MinStock,
    decimal SuggestedQuantity,
    bool AlreadyRequested);

public sealed record LowStockScanDto(
    int MaterialsBelowMinimum,
    int MissingMaterialsCreated,
    List<MissingMaterialDto> Created);

public sealed record PurchaseOrderSummaryDto(
    Guid Id,
    string Code,
    Guid SupplierId,
    string Status,
    DateTime CreatedAt,
    DateTime? ConfirmedAt,
    DateTime? ReceivedAt,
    int ItemCount,
    DateTime? ExpectedDeliveryDate);

public sealed record PurchaseOrderDetailDto(
    Guid Id,
    string Code,
    Guid SupplierId,
    string Status,
    DateTime CreatedAt,
    DateTime? ConfirmedAt,
    DateTime? ReceivedAt,
    List<PurchaseOrderItemDto> Items);

public sealed record PurchaseOrderItemDto(
    Guid Id,
    string MaterialCode,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal ReceivedQuantity,
    Guid? MissingMaterialId);

public sealed record AreaDto(Guid Id, string Name, string Code, bool IsActive, DateTime CreatedAt, Guid? SiteId = null);

public sealed record SiteDto(Guid Id, string Name, string Code, string? Address, bool IsActive);

public sealed record SiteAreaDto(Guid Id, string Name, string Code, bool IsActive);

public sealed record SiteWorkCenterDto(Guid Id, string Name, string Code, bool IsActive);

public sealed record SiteDetailDto(
    Guid Id, string Name, string Code, string? Address, bool IsActive,
    List<SiteAreaDto> Areas, List<SiteWorkCenterDto> WorkCenters);

public sealed record EquipmentDto(Guid Id, string Name, string Code, Guid? WorkCenterId, string? WorkCenterName, bool IsActive);

public sealed record EquipmentMaintenanceTaskDto(Guid Id, string Title, string Type, string Status, DateTime? DueDate, DateTime? CompletedAt);

public sealed record EquipmentDetailDto(
    Guid Id, string Name, string Code, Guid? WorkCenterId, string? WorkCenterName, bool IsActive,
    List<EquipmentMaintenanceTaskDto> Tasks);

public sealed record MaintenanceTaskDto(
    Guid Id, Guid EquipmentId, string EquipmentName, string Title, string? Description, string Type, string Status,
    DateTime? DueDate, DateTime? CompletedAt, int? RecurrenceDays, string? Notes);

public sealed record QualityCheckpointDto(
    Guid Id, Guid ProductId, string Name, string? Unit, decimal? NominalValue, decimal? LowerLimit, decimal? UpperLimit, bool IsActive);

public sealed record QualityMeasurementDto(
    Guid Id, Guid CheckpointId, string CheckpointName, string? Unit,
    Guid? WorkOrderUnitId, string? UnitSerialNumber,
    decimal MeasuredValue, decimal? LowerLimit, decimal? UpperLimit, bool? IsWithinTolerance,
    DateTime MeasuredAt, string? Notes);

public sealed record QualityCertificateDto(
    Guid WorkOrderId, string WorkOrderCode, string ProductLotNumber, string ProductCode, string ProductName,
    bool AllPassed, List<QualityMeasurementDto> Measurements);

public sealed record WorkCenterDto(Guid Id, string Code, string Name, string? Description, decimal DailyCapacityMinutes, bool IsActive);

public sealed record WorkCenterLoadDto(
    Guid? Id,
    string? Code,
    string Name,
    decimal? DailyCapacityMinutes,
    decimal PendingMinutes,
    int OpenOperations,
    decimal? BacklogDays);

public sealed record UserRowDto(Guid Id, string Name, string Email, string Role, bool IsActive, DateTime CreatedAt);

public sealed record UserAreaDto(Guid Id, string Name, string Code);

public sealed record UserActivityDto(string WorkOrderCode, string OperationName, string Kind, DateTime At);

public sealed record UserDetailDto(
    Guid Id, string Name, string Email, string Role, bool IsActive, DateTime CreatedAt,
    List<UserAreaDto> Areas, List<UserActivityDto> RecentActivity);

public sealed record SupplierDto(Guid Id, string Name, string Code, string? Email, string? Phone, string? Website, bool IsActive);

public sealed record SupplierPurchaseOrderDto(Guid Id, string Code, string Status, DateTime CreatedAt);

public sealed record SupplierCatalogEntryDto(string MaterialCode, string MaterialName, string PartNumber, string? Description, decimal UnitPrice, decimal LeadTimeDays);

public sealed record SupplierDetailDto(
    Guid Id, string Name, string Code, string? Email, string? Phone, string? Website, bool IsActive,
    List<SupplierPurchaseOrderDto> PurchaseOrders, List<SupplierCatalogEntryDto> CatalogEntries);

public sealed record CatalogSearchResultDto(
    string MaterialCode, string MaterialName, Guid SupplierId, string SupplierName, string SupplierCode,
    string? SupplierWebsite, string PartNumber, string? Description, decimal UnitPrice, decimal LeadTimeDays);

public sealed record CarrierDto(Guid Id, string Name, string Code, string? Email, string? Phone, bool IsActive);

public sealed record CarrierShipmentDto(Guid Id, string Code, string Direction, string Status, string? TrackingNumber, string? CounterpartReference, DateTime CreatedAt);

public sealed record CarrierDetailDto(Guid Id, string Name, string Code, string? Email, string? Phone, bool IsActive, List<CarrierShipmentDto> Shipments);

public sealed record AreaUserDto(Guid Id, string Name, string Email, string Role);

public sealed record AreaWorkOrderDto(Guid Id, string Code, string Status, DateTime? DueDate);

public sealed record AreaWithdrawalSlipDto(Guid Id, string Code, string Status, DateTime CreatedAt);

public sealed record AreaDetailDto(
    Guid Id, string Name, string Code, bool IsActive,
    List<AreaUserDto> Users, List<AreaWorkOrderDto> WorkOrders, List<AreaWithdrawalSlipDto> WithdrawalSlips);

public sealed record WorkCenterPendingOperationDto(
    Guid OperationId, string WorkOrderCode, string OperationName, int SequenceNumber, string Status, decimal EstimatedMinutes, DateTime? WorkOrderDueDate);

public sealed record WorkCenterDetailDto(
    Guid Id, string Code, string Name, string? Description, decimal DailyCapacityMinutes, bool IsActive,
    List<WorkCenterPendingOperationDto> PendingOperations);

public sealed record ShipmentSummaryDto(
    Guid Id,
    string Code,
    string Direction,
    string Status,
    string CarrierName,
    string? TrackingNumber,
    string? CounterpartReference,
    DateTime? ExpectedAt,
    DateTime? ShippedAt,
    DateTime? DeliveredAt,
    DateTime CreatedAt);

public sealed record ShipmentDetailDto(
    Guid Id,
    string Code,
    string Direction,
    string Status,
    Guid CarrierId,
    string CarrierName,
    string? TrackingNumber,
    Guid? PurchaseOrderId,
    string? PurchaseOrderCode,
    Guid? WorkOrderId,
    string? WorkOrderCode,
    string? CounterpartReference,
    string? Address,
    string? Notes,
    DateTime? ExpectedAt,
    DateTime? ShippedAt,
    DateTime? DeliveredAt,
    DateTime CreatedAt);

public sealed record ImportSummaryDto(int Imported, int CreatedMaterials, int CreatedLinks);

public sealed record ProductSummaryDto(Guid Id, string Code, string Name, bool IsActive, int BomItemCount, int RoutingStepCount);

public sealed record ProductDetailDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    List<BomItemDto> BillOfMaterial,
    List<RoutingStepDto> RoutingSteps);

public sealed record BomItemDto(Guid Id, string MaterialCode, decimal Quantity, string? Notes);

public sealed record RoutingStepDto(Guid Id, int SequenceNumber, string Name, string? Description, string? WorkCenter, decimal EstimatedMinutes);

public sealed record WorkOrderSummaryDto(
    Guid Id,
    string Code,
    string ProductLotNumber,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    decimal Quantity,
    string Status,
    DateTime? DueDate,
    DateTime CreatedAt,
    int OperationCount,
    int CompletedOperationCount);

public sealed record WorkOrderDetailDto(
    Guid Id,
    string Code,
    string ProductLotNumber,
    Guid ProductId,
    decimal Quantity,
    Guid? AreaId,
    string? CustomerReference,
    string Status,
    DateTime? DueDate,
    string? Notes,
    DateTime CreatedAt,
    DateTime? ReleasedAt,
    DateTime? CompletedAt,
    List<WorkOrderOperationDto> Operations);

public sealed record WorkOrderOperationDto(
    Guid Id,
    int SequenceNumber,
    string Name,
    string? Description,
    string? WorkCenter,
    decimal EstimatedMinutes,
    string Status,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    decimal? ActualMinutes,
    decimal? PerformanceRatio,
    DateTime? PlannedStartAt,
    DateTime? PlannedEndAt,
    string? StartedBy,
    string? CompletedBy);

public sealed record WorkOrderWithdrawalSlipDto(Guid WithdrawalSlipId, string WithdrawalSlipCode);

public sealed record OperationDowntimeDto(Guid Id, string Reason, string? Notes, DateTime StartedAt, DateTime? EndedAt, decimal? DurationMinutes, string? ReportedBy, string? ClosedBy);

public sealed record NonConformityDto(Guid Id, string Description, decimal ScrapQuantity, string? Notes, DateTime DetectedAt, string? ReportedBy, Guid? WorkOrderUnitId, string? UnitSerialNumber);

public sealed record WorkOrderUnitDto(Guid Id, int SequenceNumber, string SerialNumber, string Status);

public sealed record WorkOrderUnitOperationDto(Guid OperationId, int SequenceNumber, string Name, string Status, DateTime? StartedAt, DateTime? CompletedAt);

public sealed record WorkOrderUnitMaterialLotDto(Guid MaterialLotId, string MaterialCode, string LotNumber, decimal Quantity);

public sealed record PlanningEntryDto(string Type, Guid Id, string Code, string Status, DateTime Date, string Detail, string Kind);

public sealed record PlanningDashboardDto(int Overdue, int ThisWeek, int NextWeek, int Total);

public sealed record PlanningDto(DateTime RangeStart, int Weeks, IReadOnlyList<PlanningEntryDto> Entries, PlanningDashboardDto Dashboard);

public sealed record WorkOrderUnitDetailDto(
    Guid Id,
    int SequenceNumber,
    string SerialNumber,
    string Status,
    DateTime CreatedAt,
    DateTime? ResolvedAt,
    IReadOnlyList<WorkOrderUnitOperationDto> Operations,
    IReadOnlyList<WorkOrderUnitMaterialLotDto> MaterialLots);

public sealed record IdentifyOperatorDto(Guid Id, string Name);

public sealed record MaterialAvailabilityLineDto(string MaterialCode, decimal Required, decimal Available, decimal Shortfall);

public sealed record MaterialAvailabilityDto(bool IsAvailable, List<MaterialAvailabilityLineDto> Lines);

public sealed class MaterialShortfallException(MaterialAvailabilityDto availability)
    : Exception("Materiali insufficienti per questa commessa.")
{
    public MaterialAvailabilityDto Availability { get; } = availability;
}

public sealed record WorkOrderMaterialLotDto(
    Guid MaterialLotId,
    string MaterialCode,
    string LotNumber,
    decimal QuantityConsumed,
    Guid WithdrawalSlipId,
    string WithdrawalSlipCode);

public sealed record WorkOrderDashboardDto(
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

public sealed record MaterialLotSummaryDto(
    Guid Id,
    string MaterialCode,
    string LotNumber,
    decimal Quantity,
    decimal InitialQuantity,
    Guid? SupplierId,
    Guid? PurchaseOrderId,
    DateTime ReceivedAt);

public sealed record MaterialLotDetailDto(
    Guid Id,
    string MaterialCode,
    string LotNumber,
    decimal Quantity,
    decimal InitialQuantity,
    Guid? SupplierId,
    Guid? PurchaseOrderId,
    DateTime ReceivedAt,
    string? Notes,
    List<MaterialLotUsageDto> Usages);

public sealed record MaterialLotUsageDto(
    Guid ConsumptionId,
    decimal Quantity,
    DateTime ConsumedAt,
    Guid WithdrawalSlipId,
    string WithdrawalSlipCode,
    Guid? WorkOrderId);
