using System.Net.Http;
using System.Net.Http.Json;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.Json;

namespace CrmMes.Desktop;

public sealed class ApiClient
{
    private readonly HttpClient _httpClient = new();

    public Uri BaseAddress => _httpClient.BaseAddress!;

    public ApiClient()
    {
        SetBaseUrl(ClientSettings.DefaultApiBaseUrl);
    }

    /// <summary>Ripunta il client a un nuovo indirizzo API (es. dopo un cambio nelle impostazioni).</summary>
    public void SetBaseUrl(string baseUrl)
    {
        _httpClient.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
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

    public Task<IReadOnlyList<UserRowDto>> GetUsersAsync(CancellationToken cancellationToken = default)
        => GetAsync<UserRowDto>("api/users", cancellationToken);

    public Task<IReadOnlyList<SupplierDto>> GetSuppliersAsync(CancellationToken cancellationToken = default)
        => GetAsync<SupplierDto>("api/suppliers", cancellationToken);

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

    public async Task CreateSupplierAsync(string name, string code, string? email, string? phone, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/suppliers",
            new { name, code, email, phone },
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<WorkCenterDto>> GetWorkCentersAsync(CancellationToken cancellationToken = default)
        => GetAsync<WorkCenterDto>("api/work-centers", cancellationToken);

    public Task<IReadOnlyList<WorkCenterLoadDto>> GetWorkCenterLoadAsync(CancellationToken cancellationToken = default)
        => GetAsync<WorkCenterLoadDto>("api/work-centers/load", cancellationToken);

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

    public async Task CreateAreaAsync(string name, string code, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/areas", new { name, code }, cancellationToken);
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

    public Task<IReadOnlyList<WorkOrderSummaryDto>> GetWorkOrdersAsync(string? status = null, CancellationToken cancellationToken = default)
    {
        var query = "api/work-orders";
        if (!string.IsNullOrWhiteSpace(status))
        {
            query += $"?status={Uri.EscapeDataString(status)}";
        }

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

    public async Task<WorkOrderDashboardDto> GetWorkOrderDashboardAsync(int days = 7, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/work-orders/dashboard?days={days}", cancellationToken);
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

    public async Task StartOperationAsync(Guid workOrderId, Guid operationId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"api/work-orders/{workOrderId}/operations/{operationId}/start", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task CompleteOperationAsync(Guid workOrderId, Guid operationId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"api/work-orders/{workOrderId}/operations/{operationId}/complete", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<OperationDowntimeDto>> GetOperationDowntimesAsync(Guid workOrderId, Guid operationId, CancellationToken cancellationToken = default)
        => GetAsync<OperationDowntimeDto>($"api/work-orders/{workOrderId}/operations/{operationId}/downtimes", cancellationToken);

    public async Task<OperationDowntimeDto> StartDowntimeAsync(Guid workOrderId, Guid operationId, string reason, string? notes, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"api/work-orders/{workOrderId}/operations/{operationId}/downtime/start", new { reason, notes }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<OperationDowntimeDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta fermo non valida.");
    }

    public async Task EndDowntimeAsync(Guid workOrderId, Guid operationId, Guid downtimeId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync(
            $"api/work-orders/{workOrderId}/operations/{operationId}/downtime/{downtimeId}/end", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<NonConformityDto>> GetNonConformitiesAsync(Guid workOrderId, Guid operationId, CancellationToken cancellationToken = default)
        => GetAsync<NonConformityDto>($"api/work-orders/{workOrderId}/operations/{operationId}/non-conformities", cancellationToken);

    public async Task<NonConformityDto> RegisterNonConformityAsync(Guid workOrderId, Guid operationId, string description, decimal scrapQuantity, string? notes, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"api/work-orders/{workOrderId}/operations/{operationId}/non-conformities", new { description, scrapQuantity, notes }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<NonConformityDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta non conformità non valida.");
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
    int ItemCount);

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

public sealed record AreaDto(Guid Id, string Name, string Code, bool IsActive, DateTime CreatedAt);

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

public sealed record SupplierDto(Guid Id, string Name, string Code, string? Email, string? Phone, bool IsActive);

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
    DateTime? PlannedEndAt);

public sealed record WorkOrderWithdrawalSlipDto(Guid WithdrawalSlipId, string WithdrawalSlipCode);

public sealed record OperationDowntimeDto(Guid Id, string Reason, string? Notes, DateTime StartedAt, DateTime? EndedAt, decimal? DurationMinutes);

public sealed record NonConformityDto(Guid Id, string Description, decimal ScrapQuantity, string? Notes, DateTime DetectedAt);

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
