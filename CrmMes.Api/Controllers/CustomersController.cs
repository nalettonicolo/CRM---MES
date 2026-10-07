using System.Security.Claims;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using CrmMes.Core.Layout;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/customers")]
public class CustomersController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly CustomFieldService _customFields;

    public CustomersController(ApplicationDbContext dbContext, CustomFieldService customFields)
    {
        _dbContext = dbContext;
        _customFields = customFields;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CustomerResponse>>> GetCustomers(
        [FromQuery] bool activeOnly = true,
        [FromQuery] string? q = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Customers.AsNoTracking();
        if (activeOnly)
        {
            query = query.Where(customer => customer.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            // Lower-cased on both sides: a case-sensitive search silently reads as "no results".
            var term = q.Trim().ToLower();
            query = query.Where(customer =>
                customer.Name.ToLower().Contains(term) ||
                customer.Code.ToLower().Contains(term) ||
                (customer.VatNumber != null && customer.VatNumber.ToLower().Contains(term)));
        }

        var customers = await query
            .OrderBy(customer => customer.Name)
            .Select(customer => ToResponse(customer))
            .ToListAsync(cancellationToken);

        return Ok(customers);
    }

    [Authorize(Policy = "Sales")]
    [HttpPost]
    public async Task<ActionResult<CustomerResponse>> CreateCustomer(
        SaveCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim();
        var code = request.Code?.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new { message = "Nome e codice cliente sono obbligatori." });
        }

        if (await _dbContext.Customers.AnyAsync(customer => customer.Code == code, cancellationToken))
        {
            return Conflict(new { message = $"Il codice cliente '{code}' esiste già." });
        }

        var customer = new Customer { Name = name, Code = code };
        Apply(customer, request);

        var customFieldsError = await _customFields.ValidateAndStageAsync(
            FormLayoutRegistry.CustomerNew, "Customer", customer.Id, request.CustomFields, cancellationToken);
        if (customFieldsError is not null)
        {
            return BadRequest(new { message = customFieldsError });
        }

        _dbContext.Customers.Add(customer);
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "CustomerCreated",
            EntityType = "Customer",
            EntityId = customer.Id,
            UserName = GetCurrentUserName(),
            Details = $"Cliente {customer.Code} ({customer.Name}) creato."
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Created($"api/customers/{customer.Id}", ToResponse(customer));
    }

    /// <summary>Edits contact data. The code is the customer's identity on quotes and work orders, so it
    /// is not editable here.</summary>
    [Authorize(Policy = "Sales")]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CustomerResponse>> EditCustomer(
        Guid id,
        SaveCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        var customer = await _dbContext.Customers.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null)
        {
            return NotFound();
        }

        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { message = "Il nome cliente è obbligatorio." });
        }

        var customFieldsError = await _customFields.ValidateAndStageAsync(
            FormLayoutRegistry.CustomerNew, "Customer", customer.Id, request.CustomFields, cancellationToken);
        if (customFieldsError is not null)
        {
            return BadRequest(new { message = customFieldsError });
        }

        customer.Name = name;
        Apply(customer, request);
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "CustomerUpdated",
            EntityType = "Customer",
            EntityId = customer.Id,
            UserName = GetCurrentUserName(),
            Details = $"Cliente {customer.Code}: anagrafica modificata."
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(customer));
    }

    /// <summary>Customer detail in one call: registry data plus its quotes and work orders.</summary>
    [HttpGet("{id:guid}/detail")]
    public async Task<ActionResult<CustomerDetailResponse>> GetCustomerDetail(Guid id, CancellationToken cancellationToken = default)
    {
        var customer = await _dbContext.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null)
        {
            return NotFound();
        }

        // Totals computed in memory: Sqlite (test provider) can't aggregate decimals server-side.
        var quotes = (await _dbContext.Quotes.AsNoTracking()
                .Include(quote => quote.Items)
                .Where(quote => quote.CustomerId == id)
                .OrderByDescending(quote => quote.CreatedAt)
                .ToListAsync(cancellationToken))
            .Select(quote => new CustomerQuoteResponse(
                quote.Id, quote.Code, quote.Status, quote.CreatedAt, quote.ValidUntil, QuotePricing.Total(quote.Items)))
            .ToList();

        var workOrders = await _dbContext.WorkOrders.AsNoTracking()
            .Where(order => order.CustomerId == id)
            .OrderByDescending(order => order.CreatedAt)
            .Select(order => new CustomerWorkOrderResponse(
                order.Id, order.Code, order.Product.Code, order.Product.Name, order.Quantity, order.Status, order.DueDate))
            .ToListAsync(cancellationToken);

        var customFields = await _customFields.GetValuesAsync("Customer", id, cancellationToken);
        return Ok(new CustomerDetailResponse(ToResponse(customer), quotes, workOrders, customFields));
    }

    [Authorize(Policy = "Sales")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeactivateCustomer(Guid id, CancellationToken cancellationToken = default)
    {
        var customer = await _dbContext.Customers.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null)
        {
            return NotFound();
        }

        customer.IsActive = false;
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "CustomerDeactivated",
            EntityType = "Customer",
            EntityId = customer.Id,
            UserName = GetCurrentUserName(),
            Details = $"Cliente {customer.Code} disattivato."
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private string? GetCurrentUserName() => User.FindFirstValue(ClaimTypes.Name);

    private static void Apply(Customer customer, SaveCustomerRequest request)
    {
        customer.VatNumber = Clean(request.VatNumber);
        customer.Email = Clean(request.Email);
        customer.Phone = Clean(request.Phone);
        customer.Address = Clean(request.Address);
        customer.Notes = Clean(request.Notes);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CustomerResponse ToResponse(Customer customer) => new(
        customer.Id, customer.Code, customer.Name, customer.VatNumber, customer.Email,
        customer.Phone, customer.Address, customer.Notes, customer.IsActive);
}

public sealed record CustomerResponse(
    Guid Id, string Code, string Name, string? VatNumber, string? Email, string? Phone, string? Address, string? Notes, bool IsActive);

public sealed record SaveCustomerRequest(
    string? Name, string? Code, string? VatNumber, string? Email, string? Phone, string? Address, string? Notes,
    Dictionary<string, string?>? CustomFields = null);

public sealed record CustomerQuoteResponse(
    Guid Id, string Code, string Status, DateTime CreatedAt, DateTime? ValidUntil, decimal Total);

public sealed record CustomerWorkOrderResponse(
    Guid Id, string Code, string ProductCode, string ProductName, decimal Quantity, string Status, DateTime? DueDate);

public sealed record CustomerDetailResponse(
    CustomerResponse Customer, List<CustomerQuoteResponse> Quotes, List<CustomerWorkOrderResponse> WorkOrders,
    Dictionary<string, string?> CustomFields);
