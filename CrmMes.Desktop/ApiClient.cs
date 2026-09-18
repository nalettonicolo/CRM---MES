using System.Net.Http;
using System.Net.Http.Json;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.Json;

namespace CrmMes.Desktop;

public sealed class ApiClient
{
    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri("http://localhost:5092/")
    };

    public async Task<bool> EnsureLocalApiAsync(CancellationToken cancellationToken = default)
    {
        if (await TryHealthAsync(cancellationToken))
        {
            return true;
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

public sealed record UserRowDto(Guid Id, string Name, string Email, string Role, bool IsActive, DateTime CreatedAt);

public sealed record SupplierDto(Guid Id, string Name, string Code, string? Email, string? Phone, bool IsActive);

public sealed record ImportSummaryDto(int Imported, int CreatedMaterials, int CreatedLinks);
