using CrmMes.Api.Services;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Service post-vendita (module "service"): machines and panels installed at customers, the
/// requests for help that come in on them, and the interventions that answer each request. Managing the
/// registry and the requests is Admin, Management, Sales (policy "Service"); recording an intervention — the
/// technician's own visit report — is open to anyone authenticated, the same as closing a work order's phase.</summary>
[ApiController]
[Authorize]
[Route("api/service")]
public class ServiceController(ApplicationDbContext db) : ControllerBase
{
    private static readonly string[] ValidChannels = ["Phone", "Email", "Portal"];
    private static readonly string[] ValidPriorities = ["Normal", "Urgente"];

    // ---------- Installed machines ----------

    [HttpGet("machines")]
    public async Task<ActionResult<IEnumerable<InstalledMachineResponse>>> GetMachines(
        [FromQuery] bool activeOnly = true,
        [FromQuery] Guid? customerId = null,
        [FromQuery] string? q = null,
        CancellationToken cancellationToken = default)
    {
        var query = db.InstalledMachines.AsNoTracking().Include(m => m.Customer).AsQueryable();
        if (activeOnly)
        {
            query = query.Where(m => m.Status == "Active");
        }

        if (customerId is not null)
        {
            query = query.Where(m => m.CustomerId == customerId);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(m => m.SerialNumber.Contains(term) || m.Name.Contains(term) || m.Customer.Name.Contains(term));
        }

        var machines = await query.OrderBy(m => m.Name).Take(200)
            .Select(m => new InstalledMachineResponse(m.Id, m.CustomerId, m.Customer.Name, m.WorkOrderId, m.Name, m.Model, m.SerialNumber,
                m.Location, m.InstalledAt, m.WarrantyUntil, m.Status, m.Notes, m.Requests.Count(r => r.Status != ServiceRequestStatus.Closed)))
            .ToListAsync(cancellationToken);

        return Ok(machines);
    }

    [HttpGet("machines/{id:guid}")]
    public async Task<ActionResult<InstalledMachineDetailResponse>> GetMachine(Guid id, CancellationToken cancellationToken = default)
    {
        var machine = await db.InstalledMachines.AsNoTracking().Include(m => m.Customer).SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (machine is null)
        {
            return NotFound();
        }

        var requests = await db.ServiceRequests.AsNoTracking()
            .Where(r => r.InstalledMachineId == id)
            .OrderByDescending(r => r.OpenedAt)
            .Select(r => new ServiceRequestSummary(r.Id, r.Number, r.Subject, r.Priority, r.Status, r.Channel, r.OpenedAt, r.ClosedAt))
            .ToListAsync(cancellationToken);

        return Ok(new InstalledMachineDetailResponse(machine.Id, machine.CustomerId, machine.Customer.Name, machine.WorkOrderId, machine.Name,
            machine.Model, machine.SerialNumber, machine.Location, machine.InstalledAt, machine.WarrantyUntil, machine.Status, machine.Notes, requests));
    }

    [Authorize(Policy = "Service")]
    [HttpPost("machines")]
    public async Task<ActionResult<InstalledMachineResponse>> CreateMachine(CreateInstalledMachineRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim();
        var serialNumber = request.SerialNumber?.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(serialNumber))
        {
            return BadRequest(new { message = "Nome e matricola sono obbligatori." });
        }

        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == request.CustomerId && c.IsActive, cancellationToken);
        if (customer is null)
        {
            return BadRequest(new { message = "Cliente non trovato o non attivo." });
        }

        if (await db.InstalledMachines.AnyAsync(m => m.SerialNumber == serialNumber, cancellationToken))
        {
            return Conflict(new { message = "Esiste già una macchina con questa matricola." });
        }

        if (request.WorkOrderId is Guid workOrderId && !await db.WorkOrders.AnyAsync(w => w.Id == workOrderId, cancellationToken))
        {
            return BadRequest(new { message = "Commessa non trovata." });
        }

        var machine = new InstalledMachine
        {
            CustomerId = customer.Id,
            WorkOrderId = request.WorkOrderId,
            Name = name,
            Model = Clean(request.Model),
            SerialNumber = serialNumber,
            Location = Clean(request.Location),
            InstalledAt = request.InstalledAt,
            WarrantyUntil = request.WarrantyUntil,
            Notes = Clean(request.Notes),
        };
        db.InstalledMachines.Add(machine);
        AuditTrail.Add(db, User, "InstalledMachineCreated", "InstalledMachine", machine.Id, $"Macchina installata {machine.Name} registrata per {customer.Name}.");
        await db.SaveChangesAsync(cancellationToken);

        return Created($"api/service/machines/{machine.Id}", new InstalledMachineResponse(machine.Id, customer.Id, customer.Name, machine.WorkOrderId,
            machine.Name, machine.Model, machine.SerialNumber, machine.Location, machine.InstalledAt, machine.WarrantyUntil, machine.Status, machine.Notes, 0));
    }

    [Authorize(Policy = "Service")]
    [HttpPost("machines/{id:guid}/decommission")]
    public async Task<IActionResult> Decommission(Guid id, CancellationToken cancellationToken = default)
    {
        var machine = await db.InstalledMachines.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (machine is null)
        {
            return NotFound();
        }

        machine.Status = "Decommissioned";
        AuditTrail.Add(db, User, "InstalledMachineDecommissioned", "InstalledMachine", machine.Id, $"Macchina {machine.Name} dismessa.");
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // ---------- Service requests ----------

    [HttpGet("requests")]
    public async Task<ActionResult<IEnumerable<ServiceRequestResponse>>> GetRequests(
        [FromQuery] string? status = null,
        [FromQuery] Guid? installedMachineId = null,
        CancellationToken cancellationToken = default)
    {
        var query = db.ServiceRequests.AsNoTracking().Include(r => r.InstalledMachine).ThenInclude(m => m.Customer).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(r => r.Status == status.Trim());
        }

        if (installedMachineId is not null)
        {
            query = query.Where(r => r.InstalledMachineId == installedMachineId);
        }

        var requests = await query
            .OrderBy(r => r.Status == ServiceRequestStatus.Closed ? 1 : 0)
            .ThenBy(r => r.Priority == "Urgente" ? 0 : 1)
            .ThenByDescending(r => r.OpenedAt)
            .Take(200)
            .Select(r => new ServiceRequestResponse(r.Id, r.Number, r.InstalledMachineId, r.InstalledMachine.Name, r.InstalledMachine.SerialNumber,
                r.InstalledMachine.CustomerId, r.InstalledMachine.Customer.Name, r.Subject, r.Description, r.Priority, r.Status, r.Channel,
                r.RequestedBy, r.ContactInfo, r.OpenedAt, r.ClosedAt, r.Interventions.Count))
            .ToListAsync(cancellationToken);

        return Ok(requests);
    }

    [HttpGet("requests/{id:guid}")]
    public async Task<ActionResult<ServiceRequestDetailResponse>> GetRequest(Guid id, CancellationToken cancellationToken = default)
    {
        var request = await db.ServiceRequests.AsNoTracking()
            .Include(r => r.InstalledMachine).ThenInclude(m => m.Customer)
            .Include(r => r.Interventions)
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return NotFound();
        }

        return Ok(ToDetailResponse(request));
    }

    [Authorize(Policy = "Service")]
    [HttpPost("requests")]
    public async Task<ActionResult<ServiceRequestDetailResponse>> CreateRequest(CreateServiceRequestRequest request, CancellationToken cancellationToken = default)
    {
        var subject = request.Subject?.Trim();
        if (string.IsNullOrWhiteSpace(subject))
        {
            return BadRequest(new { message = "L'oggetto della richiesta è obbligatorio." });
        }

        var priority = string.IsNullOrWhiteSpace(request.Priority) ? "Normal" : request.Priority.Trim();
        if (!ValidPriorities.Contains(priority))
        {
            return BadRequest(new { message = "Priorità non valida." });
        }

        var channel = string.IsNullOrWhiteSpace(request.Channel) ? "Phone" : request.Channel.Trim();
        if (!ValidChannels.Contains(channel))
        {
            return BadRequest(new { message = "Canale non valido." });
        }

        var machine = await db.InstalledMachines.Include(m => m.Customer).SingleOrDefaultAsync(m => m.Id == request.InstalledMachineId, cancellationToken);
        if (machine is null)
        {
            return BadRequest(new { message = "Macchina installata non trovata." });
        }

        var serviceRequest = new ServiceRequest
        {
            Number = (await db.ServiceRequests.MaxAsync(r => (int?)r.Number, cancellationToken) ?? 0) + 1,
            InstalledMachineId = machine.Id,
            Subject = subject,
            Description = Clean(request.Description),
            Priority = priority,
            Channel = channel,
            RequestedBy = Clean(request.RequestedBy),
            ContactInfo = Clean(request.ContactInfo),
        };
        db.ServiceRequests.Add(serviceRequest);
        AuditTrail.Add(db, User, "ServiceRequestCreated", "ServiceRequest", serviceRequest.Id, $"Richiesta di assistenza creata per {machine.Name}.");
        await db.SaveChangesAsync(cancellationToken);

        return Created($"api/service/requests/{serviceRequest.Id}", ToDetailResponse(serviceRequest, machine));
    }

    [Authorize(Policy = "Service")]
    [HttpPost("requests/{id:guid}/close")]
    public async Task<ActionResult<ServiceRequestDetailResponse>> CloseRequest(Guid id, CancellationToken cancellationToken = default)
    {
        var request = await db.ServiceRequests.Include(r => r.InstalledMachine).ThenInclude(m => m.Customer).Include(r => r.Interventions)
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return NotFound();
        }

        if (request.Status == ServiceRequestStatus.Closed)
        {
            return Conflict(new { message = "Questa richiesta è già chiusa." });
        }

        request.Status = ServiceRequestStatus.Closed;
        request.ClosedAt = DateTime.UtcNow;
        AuditTrail.Add(db, User, "ServiceRequestClosed", "ServiceRequest", request.Id, "Richiesta di assistenza chiusa.");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToDetailResponse(request));
    }

    [Authorize(Policy = "Service")]
    [HttpPost("requests/{id:guid}/reopen")]
    public async Task<ActionResult<ServiceRequestDetailResponse>> ReopenRequest(Guid id, CancellationToken cancellationToken = default)
    {
        var request = await db.ServiceRequests.Include(r => r.InstalledMachine).ThenInclude(m => m.Customer).Include(r => r.Interventions)
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return NotFound();
        }

        if (request.Status != ServiceRequestStatus.Closed)
        {
            return Conflict(new { message = "Questa richiesta non è chiusa." });
        }

        request.Status = request.Interventions.Any() ? ServiceRequestStatus.InProgress : ServiceRequestStatus.Open;
        request.ClosedAt = null;
        AuditTrail.Add(db, User, "ServiceRequestReopened", "ServiceRequest", request.Id, "Richiesta di assistenza riaperta.");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToDetailResponse(request));
    }

    // ---------- Interventions ----------

    [HttpPost("requests/{id:guid}/interventions")]
    public async Task<ActionResult<ServiceRequestDetailResponse>> AddIntervention(Guid id, CreateServiceInterventionRequest request, CancellationToken cancellationToken = default)
    {
        var serviceRequest = await db.ServiceRequests.Include(r => r.InstalledMachine).ThenInclude(m => m.Customer).Include(r => r.Interventions)
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (serviceRequest is null)
        {
            return NotFound();
        }

        if (serviceRequest.Status == ServiceRequestStatus.Closed)
        {
            return Conflict(new { message = "La richiesta è chiusa: riaprila prima di registrare un intervento." });
        }

        if (request.Hours is < 0)
        {
            return BadRequest(new { message = "Le ore non possono essere negative." });
        }

        var intervention = new ServiceIntervention
        {
            ServiceRequestId = serviceRequest.Id,
            TechnicianName = Clean(request.TechnicianName),
            ScheduledAt = request.ScheduledAt ?? DateTime.UtcNow,
            CompletedAt = request.Completed ? DateTime.UtcNow : null,
            Hours = request.Hours,
            InWarranty = request.InWarranty,
            Description = Clean(request.Description),
            MaterialsUsed = Clean(request.MaterialsUsed),
            Notes = Clean(request.Notes),
        };
        db.ServiceInterventions.Add(intervention);

        serviceRequest.Status = ServiceRequestStatus.InProgress;
        AuditTrail.Add(db, User, "ServiceInterventionAdded", "ServiceRequest", serviceRequest.Id, "Intervento aggiunto alla richiesta di assistenza.");
        await db.SaveChangesAsync(cancellationToken);

        return Created($"api/service/requests/{id}", ToDetailResponse(serviceRequest));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ServiceRequestDetailResponse ToDetailResponse(ServiceRequest request, InstalledMachine? machine = null)
    {
        var m = machine ?? request.InstalledMachine;
        return new ServiceRequestDetailResponse(request.Id, request.Number, request.InstalledMachineId, m.Name, m.SerialNumber,
            m.CustomerId, m.Customer.Name, request.Subject, request.Description, request.Priority, request.Status, request.Channel,
            request.RequestedBy, request.ContactInfo, request.OpenedAt, request.ClosedAt,
            request.Interventions.OrderByDescending(i => i.ScheduledAt)
                .Select(i => new ServiceInterventionResponse(i.Id, i.TechnicianName, i.ScheduledAt, i.CompletedAt, i.Hours, i.InWarranty, i.Description, i.MaterialsUsed, i.Notes))
                .ToList());
    }
}

public sealed record CreateInstalledMachineRequest(Guid CustomerId, Guid? WorkOrderId, string? Name, string? Model, string? SerialNumber,
    string? Location, DateTime? InstalledAt, DateTime? WarrantyUntil, string? Notes);

public sealed record InstalledMachineResponse(Guid Id, Guid CustomerId, string CustomerName, Guid? WorkOrderId, string Name, string? Model,
    string SerialNumber, string? Location, DateTime? InstalledAt, DateTime? WarrantyUntil, string Status, string? Notes, int OpenRequestCount);

public sealed record ServiceRequestSummary(Guid Id, int Number, string Subject, string Priority, string Status, string Channel, DateTime OpenedAt, DateTime? ClosedAt);

public sealed record InstalledMachineDetailResponse(Guid Id, Guid CustomerId, string CustomerName, Guid? WorkOrderId, string Name, string? Model,
    string SerialNumber, string? Location, DateTime? InstalledAt, DateTime? WarrantyUntil, string Status, string? Notes, IReadOnlyList<ServiceRequestSummary> Requests);

public sealed record CreateServiceRequestRequest(Guid InstalledMachineId, string? Subject, string? Description, string? Priority, string? Channel,
    string? RequestedBy, string? ContactInfo);

public sealed record ServiceRequestResponse(Guid Id, int Number, Guid InstalledMachineId, string MachineName, string SerialNumber, Guid CustomerId,
    string CustomerName, string Subject, string? Description, string Priority, string Status, string Channel, string? RequestedBy, string? ContactInfo,
    DateTime OpenedAt, DateTime? ClosedAt, int InterventionCount);

public sealed record CreateServiceInterventionRequest(string? TechnicianName, DateTime? ScheduledAt, bool Completed, decimal? Hours, bool InWarranty,
    string? Description, string? MaterialsUsed, string? Notes);

public sealed record ServiceInterventionResponse(Guid Id, string? TechnicianName, DateTime ScheduledAt, DateTime? CompletedAt, decimal? Hours,
    bool InWarranty, string? Description, string? MaterialsUsed, string? Notes);

public sealed record ServiceRequestDetailResponse(Guid Id, int Number, Guid InstalledMachineId, string MachineName, string SerialNumber, Guid CustomerId,
    string CustomerName, string Subject, string? Description, string Priority, string Status, string Channel, string? RequestedBy, string? ContactInfo,
    DateTime OpenedAt, DateTime? ClosedAt, IReadOnlyList<ServiceInterventionResponse> Interventions);
