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

    /// <summary>Role of the logged-in user, set at login. Only drives what the UI offers: every
    /// restriction is enforced again by the API.</summary>
    public string? CurrentRole { get; set; }

    /// <summary>Company profile loaded at login: industry and enabled modules. Null before login and on
    /// servers without the endpoint, in which case every module counts as enabled.</summary>
    public CompanyProfileDto? CompanyProfile { get; set; }

    /// <summary>Whether a module (see Sectors in CrmMes.Api) is switched on for this company. Only
    /// decides what the UI offers: switching a module off hides it, it never deletes data.</summary>
    public bool IsModuleEnabled(string module) =>
        (CompanyProfile is null || CompanyProfile.EnabledModules.Contains(module, StringComparer.OrdinalIgnoreCase))
        && IsAreaShown(module);

    /// <summary>Areas the Admin shows in the desktop program (see AccessChannels in CrmMes.Api). Null
    /// before login and on servers without channels: everything is shown.</summary>
    public IReadOnlyList<string>? DesktopAreas { get; set; }

    public bool IsAreaShown(string area) =>
        DesktopAreas is null || DesktopAreas.Contains(area, StringComparer.OrdinalIgnoreCase);

    /// <summary>Costs, margins and hourly rates: Admin and Management only (mirrors the API's
    /// "ViewMargins" policy), and only with the costing module enabled.</summary>
    public bool CanViewMargins => CurrentRole is "Admin" or "Management" && IsModuleEnabled("costing");

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
                new { email, password, channel = "desktop" },
                cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new InvalidOperationException("Credenziali non valide.");
            }

            // 403: the Admin doesn't let this role use the desktop program; 429: account locked. Both come
            // with a message for the user, which must not be turned into "server unreachable".
            if (!response.IsSuccessStatusCode && (int)response.StatusCode < 500)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(TryExtractMessage(body) ?? $"Accesso non riuscito ({(int)response.StatusCode}).");
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

    /// <summary>Second login step with two-factor: the challenge from LoginAsync and the code.</summary>
    public async Task<AuthDto> LoginTwoFactorAsync(string challenge, string code, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync("api/auth/login/2fa", new { challenge, code }, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(TryExtractMessage(body) ?? "Codice non valido.");
            }

            return await response.Content.ReadFromJsonAsync<AuthDto>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Risposta di accesso non valida.");
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException("Server non raggiungibile.", exception);
        }
    }

    /// <summary>Support contacts: readable without login (the login screen's Teleassistenza link).</summary>
    public async Task<SupportInfoDto> GetSupportInfoAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("api/support/info", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<SupportInfoDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta di assistenza non valida.");
    }

    public Task<SupportTicketDto> SendSupportRequestAsync(string subject, string message, string? contact, string? remoteSessionId, CancellationToken cancellationToken = default)
        => SendAsync<SupportTicketDto>(HttpMethod.Post, "api/support/requests", new { subject, message, contact, remoteSessionId }, cancellationToken);

    public async Task<IReadOnlyList<SupportTicketDto>> GetSupportRequestsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("api/support/requests", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        return await response.Content.ReadFromJsonAsync<List<SupportTicketDto>>(cancellationToken: cancellationToken) ?? [];
    }

    /// <summary>Server state for the diagnostic package (Admin only), kept as raw JSON.</summary>
    public async Task<System.Text.Json.JsonElement> GetServerDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("api/support/diagnostics", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: cancellationToken);
    }

    /// <summary>The subscription state for the banner; null on a server older than licensing.</summary>
    public async Task<LicenseDto?> GetLicenseAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("api/license", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<LicenseDto>(cancellationToken: cancellationToken);
    }

    public Task<TwoFactorStatusDto> GetTwoFactorStatusAsync(CancellationToken cancellationToken = default)
        => GetOneAsync<TwoFactorStatusDto>("api/account/2fa", cancellationToken);

    public Task<TwoFactorSetupDto> StartTwoFactorSetupAsync(CancellationToken cancellationToken = default)
        => SendAsync<TwoFactorSetupDto>(HttpMethod.Post, "api/account/2fa/setup", new { }, cancellationToken);

    public async Task<List<string>> EnableTwoFactorAsync(string code, CancellationToken cancellationToken = default)
        => (await SendAsync<RecoveryCodesDto>(HttpMethod.Post, "api/account/2fa/enable", new { code }, cancellationToken)).RecoveryCodes;

    public async Task<List<string>> NewRecoveryCodesAsync(string code, CancellationToken cancellationToken = default)
        => (await SendAsync<RecoveryCodesDto>(HttpMethod.Post, "api/account/2fa/recovery-codes", new { code }, cancellationToken)).RecoveryCodes;

    public async Task DisableTwoFactorAsync(string password, string code, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/account/2fa/disable", new { password, code }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task ResetUserTwoFactorAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"api/users/{userId}/2fa/reset", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<SecuritySettingsDto> GetSecuritySettingsAsync(CancellationToken cancellationToken = default)
        => GetOneAsync<SecuritySettingsDto>("api/company-profile/security", cancellationToken);

    public Task<SecuritySettingsDto> SaveSecuritySettingsAsync(List<string> twoFactorRoles, CancellationToken cancellationToken = default)
        => SendAsync<SecuritySettingsDto>(HttpMethod.Put, "api/company-profile/security", new { twoFactorRoles }, cancellationToken);

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

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(TryExtractMessage(body) ?? "Accesso non più consentito da questo programma.");
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

    public Task<IReadOnlyList<CustomerDto>> GetCustomersAsync(bool activeOnly = true, string? q = null, CancellationToken cancellationToken = default)
    {
        var path = $"api/customers?activeOnly={activeOnly}";
        if (!string.IsNullOrWhiteSpace(q))
        {
            path += $"&q={Uri.EscapeDataString(q.Trim())}";
        }

        return GetAsync<CustomerDto>(path, cancellationToken);
    }

    public async Task<CustomerDetailDto> GetCustomerDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/customers/{id}/detail", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CustomerDetailDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta dettaglio cliente non valida.");
    }

    public async Task SaveCustomerAsync(Guid? id, SaveCustomerDto customer, CancellationToken cancellationToken = default)
    {
        using var response = id.HasValue
            ? await _httpClient.PutAsJsonAsync($"api/customers/{id}", customer, cancellationToken)
            : await _httpClient.PostAsJsonAsync("api/customers", customer, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeactivateCustomerAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync($"api/customers/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<QuoteSummaryDto>> GetQuotesAsync(string? status = null, CancellationToken cancellationToken = default)
        => GetAsync<QuoteSummaryDto>(
            string.IsNullOrWhiteSpace(status) ? "api/quotes" : $"api/quotes?status={Uri.EscapeDataString(status)}",
            cancellationToken);

    public async Task<QuoteDto> GetQuoteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/quotes/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<QuoteDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta preventivo non valida.");
    }

    public async Task<QuoteDto> SaveQuoteAsync(Guid? id, SaveQuoteDto quote, CancellationToken cancellationToken = default)
    {
        using var response = id.HasValue
            ? await _httpClient.PutAsJsonAsync($"api/quotes/{id}", quote, cancellationToken)
            : await _httpClient.PostAsJsonAsync("api/quotes", quote, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<QuoteDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta preventivo non valida.");
    }

    /// <summary>Stato del preventivo: "send", "accept" o "reject".</summary>
    public async Task ChangeQuoteStatusAsync(Guid id, string action, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"api/quotes/{id}/{action}", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<ConvertQuoteResultDto> ConvertQuoteAsync(Guid id, DateTime? dueDate, Guid? areaId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync($"api/quotes/{id}/convert", new { dueDate, areaId }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ConvertQuoteResultDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta conversione non valida.");
    }

    public async Task<ProductMaterialCostDto> GetProductMaterialCostAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/products/{productId}/material-cost", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ProductMaterialCostDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta costo materiali non valida.");
    }

    public async Task<WorkOrderCostingDto> GetWorkOrderCostingAsync(Guid workOrderId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/work-orders/{workOrderId}/costing", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<WorkOrderCostingDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta costi commessa non valida.");
    }

    public async Task SetWorkOrderSalePriceAsync(Guid workOrderId, decimal? salePrice, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync($"api/work-orders/{workOrderId}/sale-price", new { salePrice }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<LaborEntryDto>> GetLaborEntriesAsync(Guid workOrderId, CancellationToken cancellationToken = default)
        => GetAsync<LaborEntryDto>($"api/work-orders/{workOrderId}/labor", cancellationToken);

    public async Task AddLaborEntryAsync(
        Guid workOrderId, decimal minutes, DateTime workDate, Guid? workCenterId, Guid? operationId, string? notes,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"api/work-orders/{workOrderId}/labor",
            new { minutes, workDate, workCenterId, operationId, notes },
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeleteLaborEntryAsync(Guid workOrderId, Guid entryId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync($"api/work-orders/{workOrderId}/labor/{entryId}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task SetWorkCenterHourlyRateAsync(Guid workCenterId, decimal? hourlyRate, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync($"api/work-centers/{workCenterId}/hourly-rate", new { hourlyRate }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<MarginOverviewDto> GetMarginsAsync(string? status = null, int take = 50, CancellationToken cancellationToken = default)
    {
        var path = $"api/margins?take={take}";
        if (!string.IsNullOrWhiteSpace(status))
        {
            path += $"&status={Uri.EscapeDataString(status)}";
        }

        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<MarginOverviewDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta controllo margini non valida.");
    }

    public async Task<CompanyProfileDto?> GetCompanyProfileAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("api/company-profile", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // server older than the sector configuration
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CompanyProfileDto>(cancellationToken: cancellationToken);
    }

    /// <summary>Areas to show on a channel, or null on a server older than the channel settings.</summary>
    public async Task<IReadOnlyList<string>?> GetChannelAreasAsync(string channel, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/company-profile/areas?channel={channel}", cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<string>>(cancellationToken: cancellationToken);
    }

    /// <summary>Departments configured so far; empty on a server older than the company structure.</summary>
    public async Task<IReadOnlyList<DepartmentDto>> GetCompanyStructureAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("api/company-profile/structure", cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
        {
            return [];
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<DepartmentDto>>(cancellationToken: cancellationToken) ?? [];
    }

    public Task<StructureResultDto> SaveCompanyStructureAsync(List<SaveDepartmentDto> departments, CancellationToken cancellationToken = default)
        => SendAsync<StructureResultDto>(HttpMethod.Put, "api/company-profile/structure", new { departments }, cancellationToken);

    public Task<AccessChannelsDto> GetAccessChannelsAsync(CancellationToken cancellationToken = default)
        => GetOneAsync<AccessChannelsDto>("api/company-profile/access", cancellationToken);

    public Task<AccessChannelsDto> SaveAccessChannelsAsync(SaveAccessChannelsDto settings, CancellationToken cancellationToken = default)
        => SendAsync<AccessChannelsDto>(HttpMethod.Put, "api/company-profile/access", settings, cancellationToken);

    public async Task<CompanyCatalogDto> GetCompanyCatalogAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("api/company-profile/catalog", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CompanyCatalogDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta settori non valida.");
    }

    public async Task<CompanyProfileDto> SaveCompanyProfileAsync(SaveCompanyProfileDto profile, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync("api/company-profile", profile, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CompanyProfileDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta profilo azienda non valida.");
    }

    public Task<IReadOnlyList<TransportReasonDto>> GetTransportReasonsAsync(CancellationToken cancellationToken = default)
        => GetAsync<TransportReasonDto>("api/transport-documents/reasons", cancellationToken);

    public Task<IReadOnlyList<TransportDocumentSummaryDto>> GetTransportDocumentsAsync(
        string? status = null, string? reason = null, string? search = null, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query.Add($"status={Uri.EscapeDataString(status)}");
        }

        if (!string.IsNullOrWhiteSpace(reason))
        {
            query.Add($"reason={Uri.EscapeDataString(reason)}");
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Add($"search={Uri.EscapeDataString(search)}");
        }

        var path = "api/transport-documents" + (query.Count > 0 ? "?" + string.Join('&', query) : string.Empty);
        return GetAsync<TransportDocumentSummaryDto>(path, cancellationToken);
    }

    public async Task<TransportDocumentDto> GetTransportDocumentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/transport-documents/{id}", cancellationToken);
        return await ReadTransportDocumentAsync(response, cancellationToken);
    }

    public async Task<TransportDocumentDto> CreateTransportDocumentAsync(SaveTransportDocumentDto document, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/transport-documents", document, cancellationToken);
        return await ReadTransportDocumentAsync(response, cancellationToken);
    }

    public async Task<TransportDocumentDto> CreateTransportDocumentFromWorkOrderAsync(Guid workOrderId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"api/transport-documents/from-work-order/{workOrderId}", null, cancellationToken);
        return await ReadTransportDocumentAsync(response, cancellationToken);
    }

    public async Task<TransportDocumentDto> UpdateTransportDocumentAsync(Guid id, SaveTransportDocumentDto document, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync($"api/transport-documents/{id}", document, cancellationToken);
        return await ReadTransportDocumentAsync(response, cancellationToken);
    }

    public async Task DeleteTransportDocumentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync($"api/transport-documents/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<TransportDocumentDto> IssueTransportDocumentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync($"api/transport-documents/{id}/issue", new { transportStartAt = (DateTime?)null }, cancellationToken);
        return await ReadTransportDocumentAsync(response, cancellationToken);
    }

    public async Task<TransportDocumentDto> CancelTransportDocumentAsync(Guid id, string reason, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync($"api/transport-documents/{id}/cancel", new { reason }, cancellationToken);
        return await ReadTransportDocumentAsync(response, cancellationToken);
    }

    public async Task<TransportDocumentDto> AddSubcontractingReturnAsync(
        Guid documentId, Guid lineId, decimal quantity, decimal scrapQuantity, DateTime? returnedAt,
        string? supplierDocumentReference, string? notes, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"api/transport-documents/{documentId}/lines/{lineId}/returns",
            new { quantity, scrapQuantity, returnedAt, supplierDocumentReference, notes },
            cancellationToken);
        return await ReadTransportDocumentAsync(response, cancellationToken);
    }

    public async Task<TransportDocumentDto> DeleteSubcontractingReturnAsync(Guid documentId, Guid returnId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync($"api/transport-documents/{documentId}/returns/{returnId}", cancellationToken);
        return await ReadTransportDocumentAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<SubcontractingOpenLineDto>> GetOpenSubcontractingAsync(Guid? supplierId = null, CancellationToken cancellationToken = default)
        => GetAsync<SubcontractingOpenLineDto>(
            "api/subcontracting/open" + (supplierId.HasValue ? $"?supplierId={supplierId}" : string.Empty), cancellationToken);

    private static async Task<TransportDocumentDto> ReadTransportDocumentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TransportDocumentDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta DDT non valida.");
    }

    public async Task<PanelVerificationDto> GetPanelVerificationAsync(Guid workOrderId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/work-orders/{workOrderId}/panel-verification", cancellationToken);
        return await ReadPanelVerificationAsync(response, cancellationToken);
    }

    public async Task<PanelVerificationDto> SavePanelVerificationAsync(Guid workOrderId, SavePanelVerificationDto verification, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync($"api/work-orders/{workOrderId}/panel-verification", verification, cancellationToken);
        return await ReadPanelVerificationAsync(response, cancellationToken);
    }

    public async Task<PanelVerificationDto> CompletePanelVerificationAsync(Guid workOrderId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"api/work-orders/{workOrderId}/panel-verification/complete", null, cancellationToken);
        return await ReadPanelVerificationAsync(response, cancellationToken);
    }

    public async Task<PanelVerificationDto> ReopenPanelVerificationAsync(Guid workOrderId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"api/work-orders/{workOrderId}/panel-verification/reopen", null, cancellationToken);
        return await ReadPanelVerificationAsync(response, cancellationToken);
    }

    private static async Task<PanelVerificationDto> ReadPanelVerificationAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<PanelVerificationDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta verifica quadro non valida.");
    }

    public Task<IReadOnlyList<TransportDocumentExportRowDto>> GetTransportDocumentExportAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
        => GetAsync<TransportDocumentExportRowDto>($"api/transport-documents/export?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", cancellationToken);

    // ---------- Food: allergens, label, SSCC

    public Task<IReadOnlyList<AllergenDto>> GetAllergensAsync(CancellationToken cancellationToken = default)
        => GetAsync<AllergenDto>("api/food/allergens", cancellationToken);

    public Task<MaterialFoodInfoDto> GetMaterialFoodInfoAsync(Guid materialId, CancellationToken cancellationToken = default)
        => GetOneAsync<MaterialFoodInfoDto>($"api/food/materials/{materialId}", cancellationToken);

    public Task<MaterialFoodInfoDto> SaveMaterialFoodInfoAsync(Guid materialId, string? ingredientName, IReadOnlyList<string> allergens, CancellationToken cancellationToken = default)
        => SendAsync<MaterialFoodInfoDto>(HttpMethod.Put, $"api/food/materials/{materialId}", new { ingredientName, allergens }, cancellationToken);

    public Task<ProductFoodInfoDto> GetProductFoodInfoAsync(Guid productId, CancellationToken cancellationToken = default)
        => GetOneAsync<ProductFoodInfoDto>($"api/food/products/{productId}", cancellationToken);

    public Task<ProductFoodInfoDto> SaveProductFoodInfoAsync(Guid productId, SaveProductFoodInfoDto info, CancellationToken cancellationToken = default)
        => SendAsync<ProductFoodInfoDto>(HttpMethod.Put, $"api/food/products/{productId}", info, cancellationToken);

    public Task<FoodLabelDto> GetFoodLabelAsync(Guid workOrderId, CancellationToken cancellationToken = default)
        => GetOneAsync<FoodLabelDto>($"api/food/work-orders/{workOrderId}/label", cancellationToken);

    public Task<LogisticUnitDto> CreateLogisticUnitAsync(Guid? workOrderId, Guid? transportDocumentId, decimal? quantity, CancellationToken cancellationToken = default)
        => SendAsync<LogisticUnitDto>(HttpMethod.Post, "api/food/logistic-units", new { workOrderId, transportDocumentId, quantity }, cancellationToken);

    public Task<IReadOnlyList<LogisticUnitDto>> GetLogisticUnitsAsync(Guid workOrderId, CancellationToken cancellationToken = default)
        => GetAsync<LogisticUnitDto>($"api/food/logistic-units?workOrderId={workOrderId}", cancellationToken);

    public async Task SetGs1PrefixAsync(string companyPrefix, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync("api/food/gs1-prefix", new { companyPrefix }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    // ---------- Electronic invoices

    public Task<IReadOnlyList<InvoiceSummaryDto>> GetInvoicesAsync(string? status = null, CancellationToken cancellationToken = default)
        => GetAsync<InvoiceSummaryDto>("api/invoices" + (string.IsNullOrWhiteSpace(status) ? string.Empty : $"?status={Uri.EscapeDataString(status)}"), cancellationToken);

    public Task<InvoiceDto> GetInvoiceAsync(Guid id, CancellationToken cancellationToken = default)
        => GetOneAsync<InvoiceDto>($"api/invoices/{id}", cancellationToken);

    public Task<IReadOnlyList<UninvoicedDocumentDto>> GetUninvoicedDocumentsAsync(Guid? customerId = null, CancellationToken cancellationToken = default)
        => GetAsync<UninvoicedDocumentDto>("api/invoices/uninvoiced-transport-documents" + (customerId.HasValue ? $"?customerId={customerId}" : string.Empty), cancellationToken);

    public Task<InvoiceDto> CreateInvoiceFromDocumentsAsync(IReadOnlyList<Guid> transportDocumentIds, CancellationToken cancellationToken = default)
        => SendAsync<InvoiceDto>(HttpMethod.Post, "api/invoices/from-transport-documents", new { transportDocumentIds }, cancellationToken);

    public Task<InvoiceDto> SaveInvoiceAsync(Guid? id, SaveInvoiceDto invoice, CancellationToken cancellationToken = default)
        => id is { } existing
            ? SendAsync<InvoiceDto>(HttpMethod.Put, $"api/invoices/{existing}", invoice, cancellationToken)
            : SendAsync<InvoiceDto>(HttpMethod.Post, "api/invoices", invoice, cancellationToken);

    public async Task DeleteInvoiceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync($"api/invoices/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<InvoiceDto> IssueInvoiceAsync(Guid id, DateTime? issueDate, CancellationToken cancellationToken = default)
        => SendAsync<InvoiceDto>(HttpMethod.Post, $"api/invoices/{id}/issue",
            new { issueDate = issueDate.HasValue ? DateTime.SpecifyKind(issueDate.Value.Date, DateTimeKind.Utc) : (DateTime?)null }, cancellationToken);

    /// <summary>The FatturaPA XML and the file name the Exchange System expects.</summary>
    public async Task<(string FileName, byte[] Content)> DownloadInvoiceXmlAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/invoices/{id}/xml", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var name = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName ?? "fattura.xml";
        return (name.Trim('"'), await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    public Task<CustomerFiscalDto> GetCustomerFiscalAsync(Guid customerId, CancellationToken cancellationToken = default)
        => GetOneAsync<CustomerFiscalDto>($"api/customers/{customerId}/fiscal", cancellationToken);

    public Task<CustomerFiscalDto> SaveCustomerFiscalAsync(Guid customerId, CustomerFiscalDto data, CancellationToken cancellationToken = default)
        => SendAsync<CustomerFiscalDto>(HttpMethod.Put, $"api/customers/{customerId}/fiscal", data, cancellationToken);

    public Task<CompanyFiscalDto> GetCompanyFiscalAsync(CancellationToken cancellationToken = default)
        => GetOneAsync<CompanyFiscalDto>("api/company-profile/fiscal", cancellationToken);

    public Task<CompanyFiscalDto> SaveCompanyFiscalAsync(CompanyFiscalDto data, CancellationToken cancellationToken = default)
        => SendAsync<CompanyFiscalDto>(HttpMethod.Put, "api/company-profile/fiscal", data, cancellationToken);

    // ---------- Machine interconnection

    public Task<MachineDayDto> GetMachineDayAsync(Guid equipmentId, CancellationToken cancellationToken = default)
        => GetOneAsync<MachineDayDto>($"api/equipment/{equipmentId}/machine-day", cancellationToken);

    public async Task<MachineTokenDto> CreateMachineTokenAsync(Guid equipmentId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"api/equipment/{equipmentId}/machine-token", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<MachineTokenDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta token non valida.");
    }

    public async Task RevokeMachineTokenAsync(Guid equipmentId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync($"api/equipment/{equipmentId}/machine-token", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<MachineOverviewDto>> GetMachineOverviewAsync(CancellationToken cancellationToken = default)
        => GetAsync<MachineOverviewDto>("api/machine-data/overview", cancellationToken);

    // ---------- Recall

    public Task<RecallDto> GetRecallFromMaterialLotAsync(Guid lotId, CancellationToken cancellationToken = default)
        => GetOneAsync<RecallDto>($"api/recall/material-lot/{lotId}", cancellationToken);

    public Task<RecallDto> GetRecallFromProductLotAsync(string lotNumber, CancellationToken cancellationToken = default)
        => GetOneAsync<RecallDto>($"api/recall/product-lot?lotNumber={Uri.EscapeDataString(lotNumber)}", cancellationToken);

    // ---------- HACCP

    public Task<IReadOnlyList<HaccpControlPointDto>> GetHaccpControlPointsAsync(bool activeOnly = true, CancellationToken cancellationToken = default)
        => GetAsync<HaccpControlPointDto>($"api/haccp/control-points?activeOnly={activeOnly}", cancellationToken);

    public Task<HaccpControlPointDto> SaveHaccpControlPointAsync(Guid? id, SaveHaccpControlPointDto point, CancellationToken cancellationToken = default)
        => id is { } existing
            ? SendAsync<HaccpControlPointDto>(HttpMethod.Put, $"api/haccp/control-points/{existing}", point, cancellationToken)
            : SendAsync<HaccpControlPointDto>(HttpMethod.Post, "api/haccp/control-points", point, cancellationToken);

    public Task<HaccpReadingDto> AddHaccpReadingAsync(Guid controlPointId, decimal? value, bool? compliant, string? correctiveAction, string? notes, CancellationToken cancellationToken = default)
        => SendAsync<HaccpReadingDto>(HttpMethod.Post, $"api/haccp/control-points/{controlPointId}/readings",
            new { value, compliant, correctiveAction, notes, readAt = (DateTime?)null }, cancellationToken);

    public Task<IReadOnlyList<HaccpReadingDto>> GetHaccpReadingsAsync(DateTime from, DateTime to, bool nonCompliantOnly = false, CancellationToken cancellationToken = default)
        => GetAsync<HaccpReadingDto>($"api/haccp/readings?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&nonCompliantOnly={nonCompliantOnly}", cancellationToken);

    // ---------- Site reports

    public Task<IReadOnlyList<SiteReportSummaryDto>> GetSiteReportsAsync(Guid? workOrderId = null, string? status = null, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (workOrderId.HasValue)
        {
            query.Add($"workOrderId={workOrderId}");
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query.Add($"status={Uri.EscapeDataString(status)}");
        }

        return GetAsync<SiteReportSummaryDto>("api/site-reports" + (query.Count > 0 ? "?" + string.Join('&', query) : string.Empty), cancellationToken);
    }

    public Task<IReadOnlyList<SiteWorkOrderDto>> GetOpenSiteWorkOrdersAsync(CancellationToken cancellationToken = default)
        => GetAsync<SiteWorkOrderDto>("api/site-reports/open-work-orders", cancellationToken);

    public Task<SiteReportDto> GetSiteReportAsync(Guid id, CancellationToken cancellationToken = default)
        => GetOneAsync<SiteReportDto>($"api/site-reports/{id}", cancellationToken);

    public Task<SiteReportDto> SaveSiteReportAsync(Guid? id, SaveSiteReportDto report, CancellationToken cancellationToken = default)
        => id is { } existing
            ? SendAsync<SiteReportDto>(HttpMethod.Put, $"api/site-reports/{existing}", report, cancellationToken)
            : SendAsync<SiteReportDto>(HttpMethod.Post, "api/site-reports", report, cancellationToken);

    public async Task DeleteSiteReportAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync($"api/site-reports/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<SiteReportDto> SignSiteReportAsync(Guid id, string signedByName, string signatureImage, CancellationToken cancellationToken = default)
        => SendAsync<SiteReportDto>(HttpMethod.Post, $"api/site-reports/{id}/sign", new { signedByName, signatureImage }, cancellationToken);

    private async Task<T> GetOneAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta del server non valida.");
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta del server non valida.");
    }

    public async Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/auth/change-password", new { currentPassword, newPassword }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task ResetUserPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync($"api/users/{userId}/password", new { newPassword }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

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

    /// <summary>Article list (Excel or CSV): preview=true checks and counts without saving.</summary>
    public async Task<ArticleImportDto> ImportArticlesAsync(string filePath, bool preview, bool update, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        await using var stream = File.OpenRead(filePath);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            Path.GetExtension(filePath).Equals(".csv", StringComparison.OrdinalIgnoreCase)
                ? "text/csv"
                : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", Path.GetFileName(filePath));

        using var response = await _httpClient.PostAsync(
            $"api/materials/import?preview={(preview ? "true" : "false")}&update={(update ? "true" : "false")}", content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ArticleImportDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta di importazione non valida.");
    }

    public async Task<(string FileName, byte[] Content)> ExportArticlesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("api/materials/export", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var name = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName ?? "articoli.xlsx";
        return (name.Trim('"'), await response.Content.ReadAsByteArrayAsync(cancellationToken));
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
    public Task<IReadOnlyList<WorkOrderLookupDto>> LookupWorkOrdersAsync(string? q, CancellationToken cancellationToken = default)
        => GetAsync<WorkOrderLookupDto>("api/work-orders/lookup" + (string.IsNullOrWhiteSpace(q) ? string.Empty : $"?q={Uri.EscapeDataString(q.Trim())}"), cancellationToken);

    /// <summary>Open jobs of the departments of a user (the operator identified by PIN at the terminal).</summary>
    public Task<IReadOnlyList<WorkOrderLookupDto>> LookupDepartmentWorkOrdersAsync(Guid userId, CancellationToken cancellationToken = default)
        => GetAsync<WorkOrderLookupDto>($"api/work-orders/lookup?department=mine&forUser={userId}", cancellationToken);

    public async Task<IReadOnlyList<UserDepartmentDto>> GetUserDepartmentsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/areas/of-user?userId={userId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<UserDepartmentDto>>(cancellationToken: cancellationToken) ?? [];
    }

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

    public async Task CreateMaterialLotAsync(
        string materialCode, string lotNumber, decimal quantity, string? notes, DateTime? expiryDate = null, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/material-lots", new { materialCode, lotNumber, quantity, notes, expiryDate = AsUtcDate(expiryDate) }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task SetMaterialLotExpiryAsync(Guid lotId, DateTime? expiryDate, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync($"api/material-lots/{lotId}/expiry", new { expiryDate = AsUtcDate(expiryDate) }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<MaterialLotSummaryDto>> GetExpiringMaterialLotsAsync(int days = 30, CancellationToken cancellationToken = default)
        => GetAsync<MaterialLotSummaryDto>($"api/material-lots/expiring?days={days}", cancellationToken);

    /// <summary>A calendar day picked in a DatePicker, sent as that same day at UTC midnight: a use-by
    /// date is a day, not an instant, and must not shift to the day before when converted.</summary>
    private static DateTime? AsUtcDate(DateTime? day) => day.HasValue ? DateTime.SpecifyKind(day.Value.Date, DateTimeKind.Utc) : null;

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

    // ---- Planning produzione (board dipinta a mano) ----

    public Task<IReadOnlyList<PlanningCategoryDto>> GetPlanningCategoriesAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
        => GetAsync<PlanningCategoryDto>($"api/planning-board/categories?includeInactive={includeInactive}", cancellationToken);

    public async Task<PlanningCategoryDto> CreatePlanningCategoryAsync(string code, string name, string colorHex, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/planning-board/categories", new { code, name, colorHex }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<PlanningCategoryDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta categoria non valida.");
    }

    public async Task EditPlanningCategoryAsync(Guid id, string name, string colorHex, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync($"api/planning-board/categories/{id}", new { name, colorHex }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeactivatePlanningCategoryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync($"api/planning-board/categories/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task ReorderPlanningCategoriesAsync(IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync("api/planning-board/categories/reorder", new { orderedIds }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<IReadOnlyList<PlanningProjectDto>> GetPlanningProjectsAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
        => GetAsync<PlanningProjectDto>($"api/planning-board/projects?includeInactive={includeInactive}", cancellationToken);

    public async Task<PlanningProjectDto> CreatePlanningProjectAsync(string name, string? status, string? notes, Guid? workOrderId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/planning-board/projects", new { name, status, notes, workOrderId }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<PlanningProjectDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta progetto non valida.");
    }

    public async Task EditPlanningProjectAsync(Guid id, string name, string status, string? notes, Guid? workOrderId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync($"api/planning-board/projects/{id}", new { name, status, notes, workOrderId }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeactivatePlanningProjectAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync($"api/planning-board/projects/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task ReorderPlanningProjectsAsync(IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync("api/planning-board/projects/reorder", new { orderedIds }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<PlanningCellDto>> GetPlanningCellsAsync(DateTime from, int weeks, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/planning-board/cells?from={from:yyyy-MM-dd}&weeks={weeks}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<List<PlanningCellDto>>(cancellationToken: cancellationToken);
        return result ?? [];
    }

    public async Task PaintPlanningCellsAsync(Guid projectId, Guid categoryId, IReadOnlyList<DateTime> weekStarts, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync(
            "api/planning-board/cells", new { projectId, categoryId, weekStarts }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task ClearPlanningCellsAsync(Guid projectId, IReadOnlyList<DateTime> weekStarts, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "api/planning-board/cells")
        {
            Content = JsonContent.Create(new { projectId, weekStarts })
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
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

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.PaymentRequired)
        {
            // Subscription suspended: the server says why and what still works.
            throw new InvalidOperationException(TryExtractMessage(body) ?? "Abbonamento sospeso: è disponibile solo la consultazione generale.");
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(TryExtractMessage(body) ?? "Il tuo ruolo utente non ha i permessi per questa operazione.");
        }

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

public sealed record ArticleImportIssueDto(int Row, string? Code, string Message);

public sealed record ArticleImportSampleDto(int Row, string Code, string Name, string Unit, decimal? Price, decimal? VatRate, string Outcome);

public sealed record ArticleImportDto(
    bool Preview, int Rows, int Created, int Updated, int Unchanged, int Skipped,
    List<ArticleImportIssueDto> Errors, List<ArticleImportIssueDto> Warnings, int ErrorCount, int WarningCount,
    Dictionary<string, string> Columns, List<ArticleImportSampleDto> Samples);

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
    string Role,
    string? TwoFactorChallenge = null,
    bool TwoFactorSetupRequired = false);

public sealed record TwoFactorStatusDto(bool Enabled, bool Required, int RecoveryCodesLeft);

public sealed record LicenseDto(
    bool Enabled, string Status, string? Message, string? Plan, List<string>? Modules, int? MaxUsers,
    DateTime? ValidUntil, DateTime? CheckedAt, string? Customer);

public sealed record SupportTicketDto(Guid Id, int Number, string Subject, string Status, DateTime CreatedAt, string RequestedBy, string? Reply, DateTime? RepliedAt)
{
    public string StatusText => Status switch { "open" => "Aperta", "in-progress" => "In lavorazione", "closed" => "Chiusa", _ => Status };
    public string CreatedText => CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public string ReplyText => string.IsNullOrWhiteSpace(Reply) ? "In attesa di risposta" : Reply;
}

public sealed record SupportInfoDto(
    string? Name, string? Email, string? Phone, string? Hours, string? RustDeskIdServer, string? RustDeskKey,
    string? RemoteToolUrl, string ServerVersion, string Hosting);

public sealed record TwoFactorSetupDto(string Secret, string OtpAuthUri, string QrCodePng);

public sealed record RecoveryCodesDto(List<string> RecoveryCodes);

public sealed record SecuritySettingsDto(List<string> TwoFactorRoles, List<string> KnownRoles);

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

public sealed record WorkCenterDto(Guid Id, string Code, string Name, string? Description, decimal DailyCapacityMinutes, bool IsActive, Guid? SiteId = null, decimal? HourlyRate = null);

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

public sealed record CustomerDto(
    Guid Id, string Code, string Name, string? VatNumber, string? Email, string? Phone, string? Address, string? Notes, bool IsActive);

public sealed record SaveCustomerDto(
    string Name, string Code, string? VatNumber, string? Email, string? Phone, string? Address, string? Notes);

public sealed record CustomerQuoteDto(Guid Id, string Code, string Status, DateTime CreatedAt, DateTime? ValidUntil, decimal Total);

public sealed record CustomerWorkOrderDto(
    Guid Id, string Code, string ProductCode, string ProductName, decimal Quantity, string Status, DateTime? DueDate);

public sealed record CustomerDetailDto(CustomerDto Customer, List<CustomerQuoteDto> Quotes, List<CustomerWorkOrderDto> WorkOrders);

public sealed record QuoteSummaryDto(
    Guid Id, string Code, Guid CustomerId, string CustomerName, string Status,
    DateTime CreatedAt, DateTime? ValidUntil, DateTime? ConvertedAt, int ItemCount, decimal Total)
{
    public bool IsConverted => ConvertedAt.HasValue;
}

public sealed record QuoteItemDto(
    Guid Id, int SequenceNumber, Guid? ProductId, string? ProductCode, string? ProductName,
    string Description, decimal Quantity, decimal UnitPrice, decimal DiscountPercent, decimal LineTotal);

public sealed record QuoteDto(
    Guid Id, string Code, Guid CustomerId, string CustomerName, string CustomerCode, string Status,
    DateTime? ValidUntil, string? Notes, DateTime CreatedAt, DateTime? SentAt, DateTime? AcceptedAt,
    DateTime? RejectedAt, DateTime? ConvertedAt, decimal Total, List<QuoteItemDto> Items);

public sealed record SaveQuoteItemDto(Guid? ProductId, string Description, decimal Quantity, decimal UnitPrice, decimal DiscountPercent);

public sealed record SaveQuoteDto(Guid CustomerId, DateTime? ValidUntil, string? Notes, List<SaveQuoteItemDto> Items);

public sealed record ConvertedWorkOrderDto(Guid Id, string Code, string ProductCode, decimal Quantity);

public sealed record ConvertQuoteResultDto(Guid QuoteId, string QuoteCode, List<ConvertedWorkOrderDto> WorkOrders);

public sealed record MaterialCostLineDto(string MaterialCode, decimal Quantity, decimal? UnitPrice, decimal? LineCost);

public sealed record ProductMaterialCostDto(
    Guid ProductId, string ProductCode, decimal MaterialCost, int MissingPriceCount, List<MaterialCostLineDto> Lines);

public sealed record CostBreakdownDto(decimal Material, decimal Labor, decimal Total);

public sealed record MarginDto(decimal Amount, decimal? Ratio);

public sealed record WorkOrderMaterialCostDto(string MaterialCode, decimal Quantity, decimal Cost, string PriceSource, decimal UnpricedQuantity);

public sealed record LaborCostLineDto(
    string Kind, string Description, string? WorkCenter, string? Operator, decimal Minutes,
    decimal? HourlyRate, decimal? Cost, bool InProgress, Guid? LaborEntryId);

public sealed record WorkOrderCostingDto(
    Guid WorkOrderId, string WorkOrderCode, string ProductCode, string ProductName, decimal Quantity, string Status,
    decimal? SalePrice, CostBreakdownDto Estimated, CostBreakdownDto Actual,
    MarginDto? EstimatedMargin, MarginDto? ActualMargin, decimal ActualMinutes,
    List<WorkOrderMaterialCostDto> Materials, List<LaborCostLineDto> Labor, List<string> Warnings);

public sealed record LaborEntryDto(
    Guid Id, DateTime WorkDate, decimal Minutes, string? OperatorName, Guid? UserId,
    Guid? WorkCenterId, string? WorkCenterName, Guid? OperationId, string? Notes)
{
    public string HoursLabel => $"{Math.Floor(Minutes / 60):0}h {Minutes % 60:00}m";
}

public sealed record MarginRowDto(
    Guid WorkOrderId, string WorkOrderCode, string ProductName, string? CustomerName, string Status,
    decimal? SalePrice, decimal EstimatedCost, decimal ActualCost,
    decimal? EstimatedMarginRatio, decimal? ActualMargin, decimal? ActualMarginRatio, int WarningCount)
{
    /// <summary>Semaphore for the margin column: negative is a loss, under 15% needs attention.</summary>
    public string MarginLevel => ActualMarginRatio switch
    {
        null => "Unknown",
        < 0 => "Loss",
        < 0.15m => "Low",
        _ => "Good"
    };
}

public sealed record MarginOverviewDto(
    int WorkOrderCount, int PricedWorkOrderCount, decimal Revenue, decimal ActualCost, decimal Margin,
    decimal? MarginRatio, int LossMakingCount, List<MarginRowDto> WorkOrders);

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

public sealed record PlanningCategoryDto(Guid Id, string Code, string Name, string ColorHex, int SequenceNumber, bool IsActive);

public sealed record PlanningProjectDto(Guid Id, string Name, string Status, string? Notes, int SequenceNumber, bool IsActive, Guid? WorkOrderId);

public sealed record PlanningCellDto(Guid ProjectId, Guid CategoryId, DateTime WeekStart);

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
    DateTime ReceivedAt,
    DateTime? ExpiryDate = null)
{
    public bool IsExpired => ExpiryDate is { } date && date.Date < DateTime.Today && Quantity > 0;
    public bool ExpiresSoon => ExpiryDate is { } date && !IsExpired && date.Date <= DateTime.Today.AddDays(30) && Quantity > 0;
}

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

public sealed record CompanyProfileDto(
    bool IsConfigured, string CompanyName, string? VatNumber, string? Address, string? Phone, string? Email,
    string Sector, List<string> EnabledModules, string? Gs1CompanyPrefix = null, List<string>? Activities = null);

public sealed record SaveCompanyProfileDto(
    string CompanyName, string? VatNumber, string? Address, string? Phone, string? Email,
    string Sector, List<string> EnabledModules, List<string>? Activities = null);

public sealed record SectorDto(string Key, string Name, string Description, List<string> Modules, List<string>? Departments = null);

public sealed record WorkCenterTemplateDto(string Code, string Name);

public sealed record DepartmentCatalogDto(string Key, string Name, string Description, List<string> Modules, List<WorkCenterTemplateDto> WorkCenters);

public sealed record DepartmentDto(Guid Id, string Type, string Name, string Code, Guid? SiteId, int WorkCenterCount);

public sealed record SaveDepartmentDto(string Type, string Name, Guid? SiteId, bool CreateWorkCenters);

public sealed record StructureResultDto(int CreatedDepartments, int UpdatedDepartments, int CreatedWorkCenters);

public sealed record ModuleDto(string Key, string Name, string Description, bool SectorSpecific, bool Available = true);

public sealed record CompanyCatalogDto(List<SectorDto> Sectors, List<ModuleDto> Modules, List<DepartmentCatalogDto>? Departments = null);

public sealed record AccessAreaDto(string Key, string Name, string? Module);

public sealed record UserDepartmentDto(Guid Id, string Name, string Code, string? DepartmentType);

public sealed record AccessChannelsDto(
    List<string> Channels,
    Dictionary<string, List<string>> Roles,
    Dictionary<string, List<string>> Areas,
    List<AccessAreaDto> KnownAreas,
    List<string> KnownRoles);

public sealed record SaveAccessChannelsDto(
    List<string> Channels,
    Dictionary<string, List<string>> Roles,
    Dictionary<string, List<string>> Areas);

public sealed record TransportReasonDto(string Key, string Label);

public sealed record TransportDocumentSummaryDto(
    Guid Id, string DocumentCode, int? Number, int? Year, string Status, string Reason, string ReasonLabel,
    string RecipientName, DateTime? IssuedAt, DateTime CreatedAt, int LineCount, Guid? WorkOrderId)
{
    public string StatusLabel => TransportDocumentDto.StatusText(Status);
    public DateTime? IssuedAtLocal => IssuedAt?.ToLocalTime();
}

public sealed record SubcontractingReturnDto(
    Guid Id, decimal Quantity, decimal ScrapQuantity, DateTime ReturnedAt, string? SupplierDocumentReference,
    string? Notes, string? RecordedBy);

public sealed record TransportDocumentLineDto(
    Guid Id, int LineNumber, Guid? MaterialId, Guid? ProductId, string? Code, string Description, decimal Quantity,
    string Unit, string? LotNumber, string? Notes, decimal? ReturnedQuantity, decimal? ScrapQuantity,
    decimal? OutstandingQuantity, List<SubcontractingReturnDto> Returns);

public sealed record TransportDocumentDto(
    Guid Id, string DocumentCode, int? Number, int? Year, string Status, string Reason, string? ReasonDetail,
    string ReasonLabel, Guid? CustomerId, Guid? SupplierId, string RecipientName, string? RecipientAddress,
    string? RecipientVatNumber, string? DestinationAddress, string TransportBy, Guid? CarrierId, string? CarrierName,
    string? Port, string? GoodsAppearance, int? Packages, decimal? GrossWeightKg, DateTime? TransportStartAt,
    DateTime? ExpectedReturnAt, Guid? WorkOrderId, string? WorkOrderCode, string? Notes, string? CancellationReason,
    DateTime CreatedAt, string? CreatedBy, DateTime? IssuedAt, string? IssuedBy, DateTime? CancelledAt,
    List<TransportDocumentLineDto> Lines)
{
    public bool IsDraft => Status == "Draft";
    public bool IsSubcontracting => Reason == "Subcontracting";

    public static string StatusText(string status) => status switch
    {
        "Draft" => "Bozza",
        "Issued" => "Emesso",
        "Cancelled" => "Annullato",
        _ => status
    };

    public static string TransportByText(string transportBy) => transportBy switch
    {
        "Sender" => "Mittente",
        "Recipient" => "Destinatario",
        "Carrier" => "Vettore",
        _ => transportBy
    };
}

public sealed record SaveTransportDocumentLineDto(
    Guid? MaterialId, Guid? ProductId, string? Code, string? Description, decimal Quantity, string? Unit,
    string? LotNumber, string? Notes);

public sealed record SaveTransportDocumentDto(
    string Reason, string? ReasonDetail, Guid? CustomerId, Guid? SupplierId,
    string? RecipientName, string? RecipientAddress, string? RecipientVatNumber, string? DestinationAddress,
    string TransportBy, Guid? CarrierId, string? Port, string? GoodsAppearance, int? Packages, decimal? GrossWeightKg,
    DateTime? TransportStartAt, DateTime? ExpectedReturnAt, Guid? WorkOrderId, string? Notes,
    List<SaveTransportDocumentLineDto> Lines);

public sealed record SubcontractingOpenLineDto(
    Guid DocumentId, string DocumentCode, Guid LineId, Guid? SupplierId, string SupplierName, DateTime? SentAt,
    DateTime? ExpectedReturnAt, bool IsOverdue, string? Code, string Description, string Unit, decimal SentQuantity,
    decimal ReturnedQuantity, decimal ScrapQuantity, decimal OutstandingQuantity)
{
    public string StatusLabel => IsOverdue ? "In ritardo" : "Presso terzista";
}

public sealed record PanelVerificationCheckDto(string Clause, string Description, string? Result, string? Notes);

public sealed record PanelVerificationDto(
    bool IsSaved, string Status, Guid WorkOrderId, string WorkOrderCode, string ProductCode, string ProductName,
    string? ProductLotNumber, string? CustomerName,
    string Standard, string? OriginalManufacturer, string? SystemReference, string? SerialNumber,
    decimal? RatedVoltage, decimal? RatedCurrent, decimal? RatedFrequency, decimal? ShortTimeWithstandCurrent,
    decimal? ConditionalShortCircuitCurrent, string? IpRating, string? InternalSeparation, string? EarthingSystem,
    decimal? InsulationResistanceMOhm, decimal? DielectricTestVoltage, string? Notes,
    DateTime? CompletedAt, string? VerifiedBy, List<PanelVerificationCheckDto> Checks)
{
    public bool IsCompleted => Status == "Completed";

    public static string ResultText(string? result) => result switch
    {
        "Pass" => "Superata",
        "Fail" => "Non superata",
        "NotApplicable" => "Non applicabile",
        _ => "Da eseguire"
    };
}

public sealed record SavePanelVerificationDto(
    string Standard, string? OriginalManufacturer, string? SystemReference, string? SerialNumber,
    decimal? RatedVoltage, decimal? RatedCurrent, decimal? RatedFrequency, decimal? ShortTimeWithstandCurrent,
    decimal? ConditionalShortCircuitCurrent, string? IpRating, string? InternalSeparation, string? EarthingSystem,
    decimal? InsulationResistanceMOhm, decimal? DielectricTestVoltage, string? Notes,
    List<PanelVerificationCheckDto> Checks);

public sealed record TransportDocumentExportRowDto(
    int Number, int Year, DateTime IssuedAt, string Reason, string? CustomerCode, string RecipientName,
    string? RecipientVatNumber, string? WorkOrderCode, int LineNumber, string? Code, string Description,
    string Unit, decimal Quantity, string? LotNumber)
{
    public string DocumentNumber => $"{Number}/{Year}";
    public DateTime IssuedDate => IssuedAt.ToLocalTime().Date;
}

public sealed record AllergenDto(string Key, string Name);

public sealed record MaterialFoodInfoDto(Guid Id, string Code, string Name, string? IngredientName, List<string> Allergens);

public sealed record ProductFoodInfoDto(
    Guid Id, string Code, string Name, string? SalesName, int? ShelfLifeDays, bool UseByDate,
    string? StorageConditions, string? NetQuantity);

public sealed record SaveProductFoodInfoDto(
    string? SalesName, int? ShelfLifeDays, bool UseByDate, string? StorageConditions, string? NetQuantity);

public sealed record FoodLabelIngredientDto(string MaterialCode, string Name, decimal QuantityPerUnit, List<string> Allergens, List<string> AllergenNames);

public sealed record FoodLabelDto(
    Guid WorkOrderId, string WorkOrderCode, string ProductCode, string SalesName, string? LotNumber, decimal Quantity,
    DateTime ProductionDate, DateTime? ExpiryDate, bool UseByDate, string? StorageConditions, string? NetQuantity,
    string? ProducerName, string? ProducerAddress,
    List<FoodLabelIngredientDto> Ingredients, List<string> Allergens, List<string> Warnings);

public sealed record LogisticUnitDto(
    Guid Id, string Sscc, Guid? WorkOrderId, Guid? TransportDocumentId, string? ProductCode, string? ProductName,
    string? LotNumber, decimal? Quantity, DateTime? BestBefore, DateTime CreatedAt);

public sealed record RecallWorkOrderDto(
    Guid Id, string Code, string ProductCode, string ProductName, string? ProductLotNumber, decimal Quantity,
    string Status, decimal ConsumedQuantity, string? CustomerName, List<string> AffectedSerials)
{
    public string StatusLabel => StatusToItalianTextConverter.Translate(Status);
}

public sealed record RecallShipmentDto(
    Guid DocumentId, string DocumentCode, DateTime? IssuedAt, string RecipientName, string? CustomerCode,
    string? RecipientAddress, string? Code, string Description, decimal Quantity, string Unit, string? LotNumber);

public sealed record RecallPalletDto(string Sscc, string? LotNumber, decimal? Quantity, DateTime CreatedAt);

public sealed record RecallDto(
    string Subject, List<RecallWorkOrderDto> WorkOrders, List<RecallPalletDto> Pallets, List<RecallShipmentDto> Shipments,
    List<string> Customers, List<string> Warnings);

public sealed record HaccpControlPointDto(
    Guid Id, string Name, string? Location, string? Hazard, string? Unit, decimal? MinValue, decimal? MaxValue,
    string? Frequency, string? CorrectiveActionHint, bool IsActive, bool IsNumeric,
    DateTime? LastReadAt, decimal? LastValue, bool? LastCompliant)
{
    public string LimitsText => (MinValue, MaxValue) switch
    {
        (null, null) => "Sì / No",
        ({ } min, null) => $"≥ {min:0.##} {Unit}",
        (null, { } max) => $"≤ {max:0.##} {Unit}",
        ({ } min, { } max) => $"{min:0.##} – {max:0.##} {Unit}"
    };

    public string LastText => LastReadAt is null ? "Mai rilevato"
        : $"{LastReadAt.Value.ToLocalTime():dd/MM HH:mm} · {(IsNumeric ? $"{LastValue:0.##} {Unit}" : LastCompliant == true ? "Conforme" : "Non conforme")}";

    public bool LastNonCompliant => LastCompliant == false;
}

public sealed record SaveHaccpControlPointDto(
    string Name, string? Location, string? Hazard, string? Unit, decimal? MinValue, decimal? MaxValue,
    string? Frequency, string? CorrectiveActionHint, bool? IsActive = null);

public sealed record HaccpReadingDto(
    Guid Id, Guid ControlPointId, string ControlPointName, string? Location, string? Unit, decimal? MinValue,
    decimal? MaxValue, decimal? Value, bool Compliant, string? CorrectiveAction, string? Notes, DateTime ReadAt,
    string? OperatorName)
{
    public DateTime ReadAtLocal => ReadAt.ToLocalTime();
    public string ValueText => Value is { } v ? $"{v:0.##} {Unit}" : Compliant ? "Conforme" : "Non conforme";
    public string OutcomeText => Compliant ? "Conforme" : "NON CONFORME";
}

public sealed record SiteReportHoursDto(string TechnicianName, Guid? WorkCenterId, string? WorkCenterName, decimal Minutes)
{
    public string HoursText => $"{(int)(Minutes / 60)}:{(int)(Minutes % 60):00}";
}

public sealed record SiteReportMaterialDto(string? MaterialCode, string Description, decimal Quantity, string Unit);

public sealed record SiteReportSummaryDto(
    Guid Id, string Code, Guid WorkOrderId, string WorkOrderCode, string? CustomerName, string Status, DateTime WorkDate,
    decimal TotalMinutes, string? SignedByName, DateTime? SignedAt, string? CreatedBy)
{
    public string StatusLabel => Status == "Signed" ? "Firmato" : "Bozza";
    public string BrushStatus => Status == "Signed" ? "Completed" : "Draft";
    public string HoursText => $"{(int)(TotalMinutes / 60)}:{(int)(TotalMinutes % 60):00}";
}

public sealed record SiteReportDto(
    Guid Id, string Code, Guid WorkOrderId, string WorkOrderCode, string ProductName, string? CustomerName, string Status,
    DateTime WorkDate, string? SiteAddress, string Description, string? Notes, string? SignedByName, string? SignatureImage,
    DateTime? SignedAt, string? CreatedBy, DateTime CreatedAt, List<SiteReportHoursDto> Hours,
    List<SiteReportMaterialDto> Materials)
{
    public bool IsSigned => Status == "Signed";
}

public sealed record SiteReportHoursRequestDto(string TechnicianName, Guid? WorkCenterId, decimal Minutes);

public sealed record SiteReportMaterialRequestDto(string? MaterialCode, string? Description, decimal Quantity, string? Unit);

public sealed record SaveSiteReportDto(
    Guid WorkOrderId, DateTime WorkDate, string? SiteAddress, string Description, string? Notes,
    List<SiteReportHoursRequestDto> Hours, List<SiteReportMaterialRequestDto> Materials);

public sealed record SiteWorkOrderDto(Guid Id, string Code, string ProductName, string? CustomerName, string? CustomerAddress, string Status, DateTime? DueDate)
{
    public string Label => $"{Code} · {CustomerName ?? ProductName}";
}

public sealed record WorkOrderLookupDto(
    Guid Id, string Code, string ProductLotNumber, string ProductName, decimal Quantity, string Status, DateTime? DueDate,
    string? CustomerName, string? ActiveOperation)
{
    public string StatusLabel => StatusToItalianTextConverter.Translate(Status);
}

public sealed record InvoiceSummaryDto(
    Guid Id, string Code, string Status, string DocumentType, Guid CustomerId, string CustomerName, DateTime? IssueDate,
    decimal Total, DateTime CreatedAt)
{
    public string StatusLabel => Status == "Issued" ? "Emessa" : "Bozza";
    public string BrushStatus => Status == "Issued" ? "Completed" : "Draft";
    public string TypeLabel => DocumentType == "TD24" ? "Differita (DDT)" : "Immediata";
}

public sealed record InvoiceLineDto(
    Guid Id, int LineNumber, string? Code, string Description, decimal Quantity, string Unit, decimal UnitPrice,
    decimal DiscountPercent, decimal VatRate, string? VatNature, decimal LineTotal, Guid? TransportDocumentId);

public sealed record InvoiceDocumentDto(Guid Id, string DocumentCode, DateTime? IssuedAt);

public sealed record InvoiceVatSummaryDto(decimal Rate, string? Nature, decimal Taxable, decimal Tax);

public sealed record InvoiceDto(
    Guid Id, string Code, int? Number, int? Year, string Status, string DocumentType, Guid CustomerId, string CustomerName,
    DateTime? IssueDate, string PaymentMethod, DateTime? PaymentDueDate, string? Notes, List<InvoiceLineDto> Lines,
    List<InvoiceDocumentDto> TransportDocuments, List<InvoiceVatSummaryDto> VatSummary, decimal Total,
    List<string> Warnings, DateTime? IssuedAt, string? IssuedBy)
{
    public bool IsDraft => Status == "Draft";
}

public sealed record InvoiceLineRequestDto(
    string? Code, string Description, decimal Quantity, string? Unit, decimal UnitPrice, decimal DiscountPercent,
    decimal VatRate, string? VatNature, Guid? TransportDocumentId);

public sealed record SaveInvoiceDto(
    Guid? CustomerId, string PaymentMethod, DateTime? PaymentDueDate, string? Notes, List<InvoiceLineRequestDto> Lines);

public sealed record UninvoicedDocumentDto(Guid Id, string DocumentCode, DateTime? IssuedAt, Guid CustomerId, string CustomerName, string Reason);

public sealed record CustomerFiscalDto(
    string? FiscalCode, string? SdiCode, string? Pec, string? Street, string? PostalCode, string? City, string? Province, string? Country);

public sealed record CompanyFiscalDto(
    string? FiscalCode, string? TaxRegime, string? Street, string? PostalCode, string? City, string? Province, string? Country,
    string? ReaOffice, string? ReaNumber, string? Iban);

public sealed record MachineTokenDto(string Token, string TokenPrefix, string Endpoint, string Header);

public sealed record MachineAlarmDto(DateTime Timestamp, string? Code, string? Text);

public sealed record MachineDayDto(
    Guid EquipmentId, string EquipmentName, DateTime Day, bool Connected, string? TokenPrefix, DateTime? LastSeenAt, string? LastState,
    Dictionary<string, decimal> MinutesByState, decimal NoDataMinutes, long Pieces, long Scrap, decimal? Availability, int AlarmCount,
    List<MachineAlarmDto> Alarms);

public sealed record MachineOverviewDto(Guid EquipmentId, string Name, string Code, DateTime? LastSeenAt, string LastState, long? LastPieceCounter);
