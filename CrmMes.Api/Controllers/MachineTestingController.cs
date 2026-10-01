using System.Security.Claims;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Machine acceptance tests and CE marking of a work order (module "machine-testing"): FAT and SAT tests
/// with their checklist, the technical file checklist and the EU declaration of conformity.
///
/// Tests are done on the shop floor and at the customer's: anyone can open, fill in and close them, and the name
/// of who closed them is recorded; reopening a closed test is Admin-only. The technical file and the declaration
/// belong to the engineering office (Admin, Management); withdrawing an issued declaration is Admin-only.</summary>
[ApiController]
[Authorize]
[Route("api/machine-testing")]
public class MachineTestingController(ApplicationDbContext db) : ControllerBase
{
    /// <summary>The day Regulation (EU) 2023/1230 replaces Directive 2006/42/EC (art. 54).</summary>
    public static readonly DateTime RegulationAppliesFrom = new(2027, 1, 20);

    public static readonly IReadOnlyList<(string Section, string Description, string? Expected)> FatChecklist =
    [
        ("Documentazione e marcatura", "Targa con marcatura CE, costruttore, modello, matricola e anno di costruzione", "Presente e leggibile"),
        ("Documentazione e marcatura", "Istruzioni per l'uso nella lingua del paese di utilizzo", "Presenti e aggiornate"),
        ("Documentazione e marcatura", "Schemi elettrici, pneumatici e idraulici come costruito", "Corrispondenti alla macchina"),
        ("Sicurezza elettrica (EN 60204-1)", "Continuità del circuito di protezione (18.2)", "Entro i valori della norma"),
        ("Sicurezza elettrica (EN 60204-1)", "Resistenza d'isolamento (18.3)", "≥ 1 MΩ a 500 V c.c."),
        ("Sicurezza elettrica (EN 60204-1)", "Prova di tensione (18.4)", "Nessuna scarica"),
        ("Sicurezza elettrica (EN 60204-1)", "Protezione contro le tensioni residue (18.5)", "≤ 60 V entro 5 s"),
        ("Dispositivi di sicurezza", "Arresto di emergenza: arresto e ripristino senza riavvio automatico", "Conforme"),
        ("Dispositivi di sicurezza", "Ripari mobili e interblocchi: l'apertura arresta i moti pericolosi", "Conforme"),
        ("Dispositivi di sicurezza", "Barriere, tappeti e scanner di sicurezza: intervento e tempo di arresto", "Entro il tempo di progetto"),
        ("Dispositivi di sicurezza", "Funzioni di sicurezza del sistema di comando (PL/SIL di progetto)", "Verificate"),
        ("Dispositivi di sicurezza", "Comandi: selettore modalità, avviamento volontario, comandi ad azione mantenuta", "Conforme"),
        ("Prove funzionali", "Ciclo automatico a vuoto", "Senza anomalie"),
        ("Prove funzionali", "Ciclo con pezzo o materiale campione", "Pezzo conforme"),
        ("Prove funzionali", "Produttività e tempo ciclo", "Come da contratto"),
        ("Prove funzionali", "Rumorosità al posto operatore", null),
        ("Prove funzionali", "Assenza di perdite (pneumatica, idraulica, lubrificazione)", "Nessuna perdita"),
    ];

    public static readonly IReadOnlyList<(string Section, string Description, string? Expected)> SatChecklist =
    [
        ("Installazione", "Posizionamento, livellamento e fissaggio come da layout", "Conforme al layout"),
        ("Installazione", "Allacciamenti elettrici, pneumatici e idraulici alla rete del cliente", "Conformi"),
        ("Installazione", "Integrità dopo trasporto e rimontaggio", "Nessun danno"),
        ("Sicurezza", "Continuità del circuito di protezione dopo l'installazione (EN 60204-1, 18.2)", "Entro i valori della norma"),
        ("Sicurezza", "Arresto di emergenza e interblocchi dei ripari", "Conforme"),
        ("Sicurezza", "Dispositivi di sicurezza e tempi di arresto", "Entro il tempo di progetto"),
        ("Prove in produzione", "Ciclo con materiale del cliente", "Senza anomalie"),
        ("Prove in produzione", "Produttività concordata", "Come da contratto"),
        ("Prove in produzione", "Qualità del prodotto ottenuto", "Conforme alle specifiche"),
        ("Prove in produzione", "Integrazione con la linea del cliente e scambio segnali", "Conforme"),
        ("Consegna", "Formazione di operatori e manutentori", "Eseguita"),
        ("Consegna", "Consegna di istruzioni, schemi e dichiarazione di conformità", "Consegnati"),
    ];

    /// <summary>Elements of the technical file (Annex VII of Directive 2006/42/EC, Annex IV of Regulation (EU) 2023/1230).</summary>
    public static readonly IReadOnlyList<(string Code, string Description, bool Optional)> TechnicalFileElements =
    [
        ("general-description", "Descrizione generale della macchina", false),
        ("drawings", "Disegno complessivo, schemi dei circuiti di comando e descrizione del funzionamento", false),
        ("detailed-drawings", "Disegni dettagliati, note di calcolo e risultati delle prove sui requisiti essenziali", false),
        ("risk-assessment", "Valutazione dei rischi: requisiti essenziali applicabili, misure di protezione, rischi residui", false),
        ("standards", "Norme armonizzate e altre specifiche tecniche applicate", false),
        ("test-reports", "Rapporti delle prove e dei collaudi (FAT e SAT)", false),
        ("instructions", "Istruzioni per l'uso", false),
        ("safety-software", "Software delle funzioni di sicurezza e protezione da manomissioni (cybersicurezza)", true),
        ("partly-completed", "Dichiarazioni di incorporazione e istruzioni di assemblaggio delle quasi-macchine incorporate", true),
        ("component-declarations", "Dichiarazioni di conformità di componenti e macchine incorporati", true),
        ("series-production", "Misure interne per la conformità della produzione in serie", true),
    ];

    private static readonly string[] Kinds = ["FAT", "SAT"];
    private static readonly string[] Results = ["Pass", "Fail", "NotApplicable"];
    private static readonly string[] FileStatuses = ["Present", "NotApplicable"];

    /// <summary>Legal basis of a declaration issued on the given day.</summary>
    public static string LegalBasisOn(DateTime day) => day.Date >= RegulationAppliesFrom ? "2023/1230" : "2006/42/CE";

    public static string LegalBasisText(string legalBasis) => legalBasis == "2023/1230"
        ? "Regolamento (UE) 2023/1230 del Parlamento europeo e del Consiglio, del 14 giugno 2023, relativo alle macchine"
        : "Direttiva 2006/42/CE del Parlamento europeo e del Consiglio, del 17 maggio 2006, relativa alle macchine";

    // ---------- Overview ----------

    /// <summary>Work orders with their testing state, newest first; search on work order, product or customer.</summary>
    [HttpGet("work-orders")]
    public async Task<ActionResult<IEnumerable<MachineTestingSummary>>> List([FromQuery] string? search, CancellationToken cancellationToken = default)
    {
        var query = db.WorkOrders.AsNoTracking().Include(w => w.Product).Include(w => w.Customer).Where(w => w.Status != "Cancelled");
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(w => w.Code.ToLower().Contains(term) || w.Product.Code.ToLower().Contains(term)
                || w.Product.Name.ToLower().Contains(term) || (w.Customer != null && w.Customer.Name.ToLower().Contains(term))
                || (w.CustomerReference != null && w.CustomerReference.ToLower().Contains(term)));
        }

        var orders = await query.OrderByDescending(w => w.CreatedAt).Take(200).ToListAsync(cancellationToken);
        var ids = orders.Select(w => w.Id).ToList();
        var tests = await db.MachineTests.AsNoTracking().Where(t => ids.Contains(t.WorkOrderId))
            .Select(t => new { t.WorkOrderId, t.Number, t.Kind, t.Status }).ToListAsync(cancellationToken);
        var files = await db.MachineTechnicalFileItems.AsNoTracking().Where(i => ids.Contains(i.WorkOrderId))
            .Select(i => new { i.WorkOrderId, i.Optional, i.Status }).ToListAsync(cancellationToken);
        var declarations = await db.MachineDeclarations.AsNoTracking().Where(d => ids.Contains(d.WorkOrderId))
            .Select(d => new { d.WorkOrderId, d.Status, d.Number }).ToListAsync(cancellationToken);

        return Ok(orders.Select(order =>
        {
            var orderTests = tests.Where(t => t.WorkOrderId == order.Id).OrderBy(t => t.Number).ToList();
            var last = orderTests.LastOrDefault();
            var fileItems = files.Where(i => i.WorkOrderId == order.Id).ToList();
            var declaration = declarations.FirstOrDefault(d => d.WorkOrderId == order.Id);
            return new MachineTestingSummary(
                order.Id, order.Code, order.Status, order.Product.Code, order.Product.Name, order.Customer?.Name ?? order.CustomerReference,
                orderTests.Count, last?.Kind, last?.Status,
                fileItems.Count(i => i.Status is not null), fileItems.Count == 0 ? TechnicalFileElements.Count : fileItems.Count,
                declaration?.Status, declaration?.Number);
        }));
    }

    /// <summary>Everything about one machine: tests, technical file (the standard list until saved), declaration
    /// (prefilled until saved) and what is still missing to issue it.</summary>
    [HttpGet("work-orders/{workOrderId:guid}")]
    public async Task<ActionResult<MachineDossierResponse>> Get(Guid workOrderId, CancellationToken cancellationToken = default)
    {
        var dossier = await DossierAsync(workOrderId, cancellationToken);
        return dossier is null ? NotFound() : Ok(dossier);
    }

    // ---------- Tests ----------

    /// <summary>Opens a FAT or SAT with the standard checklist (editable before closing).</summary>
    [HttpPost("work-orders/{workOrderId:guid}/tests")]
    public async Task<ActionResult<MachineDossierResponse>> CreateTest(Guid workOrderId, CreateMachineTestRequest request, CancellationToken cancellationToken = default)
    {
        var workOrder = await db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workOrderId, cancellationToken);
        if (workOrder is null)
        {
            return NotFound();
        }

        if (workOrder.Status == "Cancelled")
        {
            return BadRequest(new { message = "La commessa è annullata: non si possono aprire collaudi." });
        }

        var kind = request.Kind?.Trim().ToUpperInvariant();
        if (kind is null || !Kinds.Contains(kind))
        {
            return BadRequest(new { message = "Tipo di collaudo non valido: FAT (in fabbrica) o SAT (presso il cliente)." });
        }

        if (await db.MachineTests.AnyAsync(t => t.WorkOrderId == workOrderId && t.Kind == kind && t.Status == "Draft", cancellationToken))
        {
            return Conflict(new { message = $"C'è già un collaudo {kind} in corso per questa commessa: chiudilo prima di aprirne un altro." });
        }

        var test = new MachineTest
        {
            Number = (await db.MachineTests.MaxAsync(t => (int?)t.Number, cancellationToken) ?? 0) + 1,
            WorkOrderId = workOrderId,
            Kind = kind,
            SerialNumber = Clean(request.SerialNumber),
            Location = Clean(request.Location) ?? (kind == "FAT" ? "Stabilimento del costruttore" : null),
            TestDate = DateTime.UtcNow.Date,
            CreatedBy = User.FindFirstValue(ClaimTypes.Name),
        };
        db.MachineTests.Add(test);
        var sequence = 1;
        foreach (var (section, description, expected) in kind == "FAT" ? FatChecklist : SatChecklist)
        {
            db.MachineTestItems.Add(new MachineTestItem { MachineTestId = test.Id, Sequence = sequence++, Section = section, Description = description, Expected = expected });
        }

        await db.SaveChangesAsync(cancellationToken);
        return Ok(await DossierAsync(workOrderId, cancellationToken));
    }

    [HttpPut("tests/{testId:guid}")]
    public async Task<ActionResult<MachineDossierResponse>> SaveTest(Guid testId, SaveMachineTestRequest request, CancellationToken cancellationToken = default)
    {
        var test = await db.MachineTests.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == testId, cancellationToken);
        if (test is null)
        {
            return NotFound();
        }

        if (test.Status != "Draft")
        {
            return Conflict(new { message = "Collaudo già chiuso: solo un Admin può riaprirlo." });
        }

        var items = request.Items ?? [];
        if (items.Count == 0)
        {
            return BadRequest(new { message = "Il collaudo deve avere almeno una verifica." });
        }

        if (items.Any(item => string.IsNullOrWhiteSpace(item.Description)))
        {
            return BadRequest(new { message = "Ogni verifica deve avere una descrizione." });
        }

        if (items.Any(item => item.Result is not null && !Results.Contains(item.Result)))
        {
            return BadRequest(new { message = "Esito non valido: Superata, Non superata o Non applicabile." });
        }

        test.SerialNumber = Clean(request.SerialNumber);
        test.Location = Clean(request.Location);
        test.TestDate = request.TestDate is { } day ? DateTime.SpecifyKind(day.Date, DateTimeKind.Utc) : null; // a calendar day, stored as UTC midnight
        test.CustomerWitness = Clean(request.CustomerWitness);
        test.Notes = Clean(request.Notes);
        test.UpdatedAt = DateTime.UtcNow;

        db.MachineTestItems.RemoveRange(test.Items);
        var sequence = 1;
        foreach (var item in items)
        {
            // Preset Guid key: added through the set, not the tracked collection, so EF inserts it.
            db.MachineTestItems.Add(new MachineTestItem
            {
                MachineTestId = test.Id,
                Sequence = sequence++,
                Section = Clean(item.Section) ?? "Altre verifiche",
                Description = item.Description!.Trim(),
                Expected = Clean(item.Expected),
                Measured = Clean(item.Measured),
                Result = item.Result,
                Notes = Clean(item.Notes),
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return Ok(await DossierAsync(test.WorkOrderId, cancellationToken));
    }

    /// <summary>Closes the test: every verification needs an outcome; any failure closes it as failed (a record of
    /// what went wrong — fix the machine and open a new test).</summary>
    [HttpPost("tests/{testId:guid}/close")]
    public async Task<ActionResult<MachineDossierResponse>> CloseTest(Guid testId, CancellationToken cancellationToken = default)
    {
        var test = await db.MachineTests.Include(t => t.Items).Include(t => t.WorkOrder).FirstOrDefaultAsync(t => t.Id == testId, cancellationToken);
        if (test is null)
        {
            return NotFound();
        }

        if (test.Status != "Draft")
        {
            return Conflict(new { message = "Collaudo già chiuso." });
        }

        var problems = new List<string>();
        if (test.Items.Count == 0 || test.Items.Any(item => item.Result is null))
        {
            problems.Add("tutte le verifiche devono avere un esito");
        }

        if (test.SerialNumber is null)
        {
            problems.Add("manca la matricola della macchina");
        }

        if (test.TestDate is null)
        {
            problems.Add("manca la data del collaudo");
        }

        if (problems.Count > 0)
        {
            return BadRequest(new { message = $"Impossibile chiudere il collaudo: {string.Join("; ", problems)}." });
        }

        test.Status = test.Items.Any(item => item.Result == "Fail") ? "Failed" : "Passed";
        test.ClosedAt = DateTime.UtcNow;
        test.TestedBy = User.FindFirstValue(ClaimTypes.Name);
        db.AuditLogs.Add(new AuditLog
        {
            Action = "MachineTestClosed",
            EntityType = "WorkOrder",
            EntityId = test.WorkOrderId,
            UserName = test.TestedBy,
            Details = $"Collaudo {test.Kind} COL {test.Number} della commessa {test.WorkOrder.Code}, matricola {test.SerialNumber}: {(test.Status == "Passed" ? "superato" : "non superato")}."
        });
        await db.SaveChangesAsync(cancellationToken);
        return Ok(await DossierAsync(test.WorkOrderId, cancellationToken));
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost("tests/{testId:guid}/reopen")]
    public async Task<ActionResult<MachineDossierResponse>> ReopenTest(Guid testId, CancellationToken cancellationToken = default)
    {
        var test = await db.MachineTests.Include(t => t.WorkOrder).FirstOrDefaultAsync(t => t.Id == testId, cancellationToken);
        if (test is null)
        {
            return NotFound();
        }

        if (await db.MachineDeclarations.AnyAsync(d => d.WorkOrderId == test.WorkOrderId && d.Status == "Issued", cancellationToken))
        {
            return Conflict(new { message = "La dichiarazione di conformità è già emessa: ritirala prima di riaprire un collaudo." });
        }

        test.Status = "Draft";
        test.ClosedAt = null;
        test.TestedBy = null;
        db.AuditLogs.Add(new AuditLog
        {
            Action = "MachineTestReopened",
            EntityType = "WorkOrder",
            EntityId = test.WorkOrderId,
            UserName = User.FindFirstValue(ClaimTypes.Name),
            Details = $"Collaudo {test.Kind} COL {test.Number} riaperto per la commessa {test.WorkOrder.Code}."
        });
        await db.SaveChangesAsync(cancellationToken);
        return Ok(await DossierAsync(test.WorkOrderId, cancellationToken));
    }

    // ---------- Technical file ----------

    /// <summary>Saves the technical file checklist. Standard elements always stay; extra elements (no code) can be
    /// added and are removed when left out. Only optional elements can be "not applicable".</summary>
    [Authorize(Policy = "Engineering")]
    [HttpPut("work-orders/{workOrderId:guid}/technical-file")]
    public async Task<ActionResult<MachineDossierResponse>> SaveTechnicalFile(Guid workOrderId, SaveTechnicalFileRequest request, CancellationToken cancellationToken = default)
    {
        if (!await db.WorkOrders.AnyAsync(w => w.Id == workOrderId, cancellationToken))
        {
            return NotFound();
        }

        if (await db.MachineDeclarations.AnyAsync(d => d.WorkOrderId == workOrderId && d.Status == "Issued", cancellationToken))
        {
            return Conflict(new { message = "Dichiarazione di conformità già emessa: il fascicolo tecnico non si modifica più." });
        }

        var items = request.Items ?? [];
        if (items.Any(item => item.Status is not null && !FileStatuses.Contains(item.Status)))
        {
            return BadRequest(new { message = "Stato non valido: Presente o Non applicabile." });
        }

        var standard = TechnicalFileElements.ToDictionary(e => e.Code);
        if (items.Any(item => item.Status == "NotApplicable" && item.Code is not null && standard.TryGetValue(item.Code, out var element) && !element.Optional))
        {
            return BadRequest(new { message = "Questo elemento del fascicolo è obbligatorio: non può essere \"non applicabile\"." });
        }

        if (items.Any(item => string.IsNullOrWhiteSpace(item.Code) && string.IsNullOrWhiteSpace(item.Description)))
        {
            return BadRequest(new { message = "Gli elementi aggiunti devono avere una descrizione." });
        }

        var existing = await db.MachineTechnicalFileItems.Where(i => i.WorkOrderId == workOrderId).ToListAsync(cancellationToken);
        var user = User.FindFirstValue(ClaimTypes.Name);
        var now = DateTime.UtcNow;

        // Standard elements: always all of them, in their order.
        var sequence = 1;
        foreach (var (code, description, optional) in TechnicalFileElements)
        {
            var sent = items.FirstOrDefault(item => item.Code == code);
            var entity = existing.FirstOrDefault(i => i.Code == code);
            if (entity is null)
            {
                entity = new MachineTechnicalFileItem { WorkOrderId = workOrderId, Code = code };
                db.MachineTechnicalFileItems.Add(entity);
            }

            Apply(entity, sequence++, description, optional, sent);
        }

        // Extra elements: kept when sent back by code, new ones without a code.
        var keptCustom = new HashSet<string>();
        foreach (var sent in items.Where(item => item.Code is null || !standard.ContainsKey(item.Code)))
        {
            var entity = sent.Code is null ? null : existing.FirstOrDefault(i => i.Code == sent.Code);
            if (entity is null)
            {
                if (string.IsNullOrWhiteSpace(sent.Description))
                {
                    continue;
                }

                entity = new MachineTechnicalFileItem { WorkOrderId = workOrderId, Code = $"custom-{Guid.NewGuid():N}"[..15] };
                db.MachineTechnicalFileItems.Add(entity);
            }

            keptCustom.Add(entity.Code);
            Apply(entity, sequence++, Clean(sent.Description) ?? entity.Description, true, sent);
        }

        db.MachineTechnicalFileItems.RemoveRange(existing.Where(i => !standard.ContainsKey(i.Code) && !keptCustom.Contains(i.Code)));
        await db.SaveChangesAsync(cancellationToken);
        return Ok(await DossierAsync(workOrderId, cancellationToken));

        void Apply(MachineTechnicalFileItem entity, int order, string description, bool optional, TechnicalFileItemRequest? sent)
        {
            var status = sent?.Status;
            var reference = Clean(sent?.Reference);
            if (entity.Status != status || entity.Reference != reference || entity.Sequence != order || entity.Description != description)
            {
                entity.UpdatedBy = user;
                entity.UpdatedAt = now;
            }

            entity.Sequence = order;
            entity.Description = description;
            entity.Optional = optional;
            entity.Status = status;
            entity.Reference = reference;
        }
    }

    // ---------- Declaration of conformity ----------

    [Authorize(Policy = "Engineering")]
    [HttpPut("work-orders/{workOrderId:guid}/declaration")]
    public async Task<ActionResult<MachineDossierResponse>> SaveDeclaration(Guid workOrderId, SaveMachineDeclarationRequest request, CancellationToken cancellationToken = default)
    {
        if (!await db.WorkOrders.AnyAsync(w => w.Id == workOrderId, cancellationToken))
        {
            return NotFound();
        }

        var declaration = await db.MachineDeclarations.FirstOrDefaultAsync(d => d.WorkOrderId == workOrderId, cancellationToken);
        if (declaration?.Status == "Issued")
        {
            return Conflict(new { message = "Dichiarazione già emessa: solo un Admin può ritirarla." });
        }

        if (string.IsNullOrWhiteSpace(request.MachineName))
        {
            return BadRequest(new { message = "Indica la denominazione della macchina." });
        }

        if (request.YearOfConstruction is { } year && (year < 1950 || year > DateTime.UtcNow.Year + 1))
        {
            return BadRequest(new { message = "Anno di costruzione non valido." });
        }

        if (declaration is null)
        {
            declaration = new MachineDeclaration { WorkOrderId = workOrderId };
            db.MachineDeclarations.Add(declaration);
        }

        declaration.MachineName = request.MachineName.Trim();
        declaration.Function = Clean(request.Function);
        declaration.Model = Clean(request.Model);
        declaration.Type = Clean(request.Type);
        declaration.SerialNumber = Clean(request.SerialNumber);
        declaration.YearOfConstruction = request.YearOfConstruction;
        declaration.OtherLegislation = Clean(request.OtherLegislation);
        declaration.Standards = Clean(request.Standards);
        declaration.NotifiedBody = Clean(request.NotifiedBody);
        declaration.TechnicalFileKeeper = Clean(request.TechnicalFileKeeper);
        declaration.Place = Clean(request.Place);
        declaration.SignatoryName = Clean(request.SignatoryName);
        declaration.SignatoryRole = Clean(request.SignatoryRole);
        declaration.Notes = Clean(request.Notes);
        declaration.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(await DossierAsync(workOrderId, cancellationToken));
    }

    /// <summary>Issues the declaration: numbered, frozen, with the legal basis in force today. Needs the data the
    /// declaration can't do without, a passed test of that serial number and a complete technical file.</summary>
    [Authorize(Policy = "Engineering")]
    [HttpPost("work-orders/{workOrderId:guid}/declaration/issue")]
    public async Task<ActionResult<MachineDossierResponse>> IssueDeclaration(Guid workOrderId, CancellationToken cancellationToken = default)
    {
        var workOrder = await db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workOrderId, cancellationToken);
        var declaration = await db.MachineDeclarations.FirstOrDefaultAsync(d => d.WorkOrderId == workOrderId, cancellationToken);
        if (workOrder is null)
        {
            return NotFound();
        }

        if (declaration is null)
        {
            return BadRequest(new { message = "Compila e salva prima la dichiarazione." });
        }

        if (declaration.Status == "Issued")
        {
            return Conflict(new { message = "Dichiarazione già emessa." });
        }

        var problems = await MissingForDeclarationAsync(workOrderId, declaration, cancellationToken);
        if (problems.Count > 0)
        {
            return BadRequest(new { message = $"Impossibile emettere la dichiarazione: {string.Join("; ", problems)}." });
        }

        var now = DateTime.UtcNow;
        declaration.Status = "Issued";
        declaration.Number = (await db.MachineDeclarations.MaxAsync(d => d.Number, cancellationToken) ?? 0) + 1;
        declaration.LegalBasis = LegalBasisOn(now);
        declaration.IssuedAt = now;
        declaration.IssuedBy = User.FindFirstValue(ClaimTypes.Name);
        db.AuditLogs.Add(new AuditLog
        {
            Action = "MachineDeclarationIssued",
            EntityType = "WorkOrder",
            EntityId = workOrderId,
            UserName = declaration.IssuedBy,
            Details = $"Dichiarazione di conformità n. {declaration.Number} ({declaration.LegalBasis}) emessa per la commessa {workOrder.Code}, matricola {declaration.SerialNumber}."
        });
        await db.SaveChangesAsync(cancellationToken);
        return Ok(await DossierAsync(workOrderId, cancellationToken));
    }

    /// <summary>Back to draft (e.g. a wrong serial number). The number is kept, so the reissued declaration has the
    /// same number; the withdrawal stays in the audit log.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPost("work-orders/{workOrderId:guid}/declaration/withdraw")]
    public async Task<ActionResult<MachineDossierResponse>> WithdrawDeclaration(Guid workOrderId, WithdrawDeclarationRequest request, CancellationToken cancellationToken = default)
    {
        var workOrder = await db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workOrderId, cancellationToken);
        var declaration = await db.MachineDeclarations.FirstOrDefaultAsync(d => d.WorkOrderId == workOrderId, cancellationToken);
        if (workOrder is null || declaration is null)
        {
            return NotFound();
        }

        if (declaration.Status != "Issued")
        {
            return Conflict(new { message = "La dichiarazione non è emessa." });
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { message = "Indica il motivo del ritiro." });
        }

        declaration.Status = "Draft";
        declaration.IssuedAt = null;
        declaration.IssuedBy = null;
        declaration.LegalBasis = null;
        db.AuditLogs.Add(new AuditLog
        {
            Action = "MachineDeclarationWithdrawn",
            EntityType = "WorkOrder",
            EntityId = workOrderId,
            UserName = User.FindFirstValue(ClaimTypes.Name),
            Details = $"Dichiarazione di conformità n. {declaration.Number} ritirata per la commessa {workOrder.Code}: {request.Reason.Trim()}"
        });
        await db.SaveChangesAsync(cancellationToken);
        return Ok(await DossierAsync(workOrderId, cancellationToken));
    }

    // ---------- Helpers ----------

    private async Task<List<string>> MissingForDeclarationAsync(Guid workOrderId, MachineDeclaration? declaration, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var profile = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (profile is null || string.IsNullOrWhiteSpace(profile.CompanyName) || ManufacturerAddress(profile) is null)
        {
            problems.Add("mancano ragione sociale e indirizzo del costruttore (Configurazione azienda)");
        }

        if (declaration is null)
        {
            problems.Add("la dichiarazione non è ancora compilata");
        }
        else
        {
            var missing = new List<string>();
            if (declaration.SerialNumber is null) missing.Add("matricola");
            if (declaration.YearOfConstruction is null) missing.Add("anno di costruzione");
            if (declaration.Function is null) missing.Add("funzione");
            if (declaration.TechnicalFileKeeper is null) missing.Add("persona autorizzata a costituire il fascicolo tecnico");
            if (declaration.Standards is null) missing.Add("norme applicate");
            if (declaration.Place is null) missing.Add("luogo");
            if (declaration.SignatoryName is null) missing.Add("firmatario");
            if (missing.Count > 0)
            {
                problems.Add($"mancano {string.Join(", ", missing)}");
            }

            var serial = declaration.SerialNumber;
            var passed = await db.MachineTests.AsNoTracking()
                .Where(t => t.WorkOrderId == workOrderId && t.Status == "Passed")
                .Select(t => t.SerialNumber).ToListAsync(cancellationToken);
            if (serial is not null && !passed.Any(s => string.Equals(s, serial, StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add($"serve un collaudo (FAT o SAT) superato della matricola {serial}");
            }
        }

        var file = await db.MachineTechnicalFileItems.AsNoTracking().Where(i => i.WorkOrderId == workOrderId).ToListAsync(cancellationToken);
        if (!IsFileComplete(file))
        {
            problems.Add("il fascicolo tecnico non è completo");
        }

        return problems;
    }

    private static bool IsFileComplete(IReadOnlyCollection<MachineTechnicalFileItem> items) =>
        items.Count > 0
        && TechnicalFileElements.All(element => items.Any(i => i.Code == element.Code))
        && items.All(i => i.Status == "Present" || (i.Optional && i.Status == "NotApplicable"));

    private static string? ManufacturerAddress(CompanyProfile profile)
    {
        var city = string.Join(" ", new[] { profile.PostalCode, profile.City, profile.Province is null ? null : $"({profile.Province})" }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        var parts = new[] { profile.Street, city }.Where(part => !string.IsNullOrWhiteSpace(part)).ToList();
        if (parts.Count > 0)
        {
            return string.Join(", ", parts);
        }

        return string.IsNullOrWhiteSpace(profile.Address) ? null : profile.Address.Trim();
    }

    private async Task<MachineDossierResponse?> DossierAsync(Guid workOrderId, CancellationToken cancellationToken)
    {
        var workOrder = await db.WorkOrders.AsNoTracking().Include(w => w.Product).Include(w => w.Customer)
            .FirstOrDefaultAsync(w => w.Id == workOrderId, cancellationToken);
        if (workOrder is null)
        {
            return null;
        }

        var profile = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var tests = await db.MachineTests.AsNoTracking().Include(t => t.Items).Where(t => t.WorkOrderId == workOrderId)
            .OrderBy(t => t.Number).ToListAsync(cancellationToken);
        var file = await db.MachineTechnicalFileItems.AsNoTracking().Where(i => i.WorkOrderId == workOrderId)
            .OrderBy(i => i.Sequence).ToListAsync(cancellationToken);
        var declaration = await db.MachineDeclarations.AsNoTracking().FirstOrDefaultAsync(d => d.WorkOrderId == workOrderId, cancellationToken);

        var fileItems = file.Count > 0
            ? file.Select(i => new TechnicalFileItemResponse(i.Code, i.Description, i.Optional, i.Status, i.Reference, i.UpdatedBy, i.UpdatedAt)).ToList()
            : TechnicalFileElements.Select(e => new TechnicalFileItemResponse(e.Code, e.Description, e.Optional, null, null, null, null)).ToList();

        var manufacturerAddress = profile is null ? null : ManufacturerAddress(profile);
        var lastSerial = tests.LastOrDefault(t => t.SerialNumber is not null)?.SerialNumber;
        var draft = declaration ?? new MachineDeclaration
        {
            WorkOrderId = workOrderId,
            MachineName = workOrder.Product.Name,
            Model = workOrder.Product.Code,
            SerialNumber = lastSerial,
            YearOfConstruction = DateTime.UtcNow.Year,
            OtherLegislation = "Direttiva 2014/30/UE (compatibilità elettromagnetica)",
            Standards = "EN ISO 12100:2010 - Sicurezza del macchinario, valutazione e riduzione del rischio\nEN 60204-1:2018 - Equipaggiamento elettrico delle macchine",
            TechnicalFileKeeper = profile is null ? null : string.Join(", ", new[] { profile.CompanyName, manufacturerAddress }.Where(p => !string.IsNullOrWhiteSpace(p))),
            Place = profile?.City,
        };
        var legalBasis = draft.LegalBasis ?? LegalBasisOn(DateTime.UtcNow);

        return new MachineDossierResponse(
            workOrder.Id, workOrder.Code, workOrder.Status, workOrder.Product.Code, workOrder.Product.Name, workOrder.ProductRevision,
            workOrder.Customer?.Name ?? workOrder.CustomerReference,
            new ManufacturerResponse(profile?.CompanyName, manufacturerAddress, profile?.VatNumber),
            tests.Select(ToResponse).ToList(),
            fileItems,
            file.Count > 0 && IsFileComplete(file),
            new MachineDeclarationResponse(
                declaration is not null, draft.Status, draft.Number, legalBasis, LegalBasisText(legalBasis),
                draft.MachineName, draft.Function, draft.Model, draft.Type, draft.SerialNumber, draft.YearOfConstruction,
                draft.OtherLegislation, draft.Standards, draft.NotifiedBody, draft.TechnicalFileKeeper, draft.Place,
                draft.SignatoryName, draft.SignatoryRole, draft.Notes, draft.IssuedAt, draft.IssuedBy),
            draft.Status == "Issued" ? [] : await MissingForDeclarationAsync(workOrderId, declaration, cancellationToken));
    }

    private static MachineTestResponse ToResponse(MachineTest test) => new(
        test.Id, test.Number, test.Kind, test.Status, test.SerialNumber, test.Location, test.TestDate, test.CustomerWitness, test.Notes,
        test.CreatedBy, test.CreatedAt, test.ClosedAt, test.TestedBy,
        test.Items.OrderBy(i => i.Sequence)
            .Select(i => new MachineTestItemResponse(i.Sequence, i.Section, i.Description, i.Expected, i.Measured, i.Result, i.Notes))
            .ToList());

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record MachineTestingSummary(
    Guid WorkOrderId, string WorkOrderCode, string WorkOrderStatus, string ProductCode, string ProductName, string? CustomerName,
    int Tests, string? LastTestKind, string? LastTestStatus, int FileDone, int FileTotal, string? DeclarationStatus, int? DeclarationNumber);

public sealed record CreateMachineTestRequest(string? Kind, string? SerialNumber, string? Location);

public sealed record MachineTestItemRequest(string? Section, string? Description, string? Expected, string? Measured, string? Result, string? Notes);

public sealed record SaveMachineTestRequest(
    string? SerialNumber, string? Location, DateTime? TestDate, string? CustomerWitness, string? Notes, List<MachineTestItemRequest>? Items);

public sealed record TechnicalFileItemRequest(string? Code, string? Description, string? Status, string? Reference);

public sealed record SaveTechnicalFileRequest(List<TechnicalFileItemRequest>? Items);

public sealed record SaveMachineDeclarationRequest(
    string? MachineName, string? Function, string? Model, string? Type, string? SerialNumber, int? YearOfConstruction,
    string? OtherLegislation, string? Standards, string? NotifiedBody, string? TechnicalFileKeeper, string? Place,
    string? SignatoryName, string? SignatoryRole, string? Notes);

public sealed record WithdrawDeclarationRequest(string? Reason);

public sealed record MachineTestItemResponse(int Sequence, string Section, string Description, string? Expected, string? Measured, string? Result, string? Notes);

public sealed record MachineTestResponse(
    Guid Id, int Number, string Kind, string Status, string? SerialNumber, string? Location, DateTime? TestDate, string? CustomerWitness,
    string? Notes, string? CreatedBy, DateTime CreatedAt, DateTime? ClosedAt, string? TestedBy, List<MachineTestItemResponse> Items);

public sealed record TechnicalFileItemResponse(
    string Code, string Description, bool Optional, string? Status, string? Reference, string? UpdatedBy, DateTime? UpdatedAt);

public sealed record ManufacturerResponse(string? Name, string? Address, string? VatNumber);

public sealed record MachineDeclarationResponse(
    bool IsSaved, string Status, int? Number, string LegalBasis, string LegalBasisText,
    string MachineName, string? Function, string? Model, string? Type, string? SerialNumber, int? YearOfConstruction,
    string? OtherLegislation, string? Standards, string? NotifiedBody, string? TechnicalFileKeeper, string? Place,
    string? SignatoryName, string? SignatoryRole, string? Notes, DateTime? IssuedAt, string? IssuedBy);

public sealed record MachineDossierResponse(
    Guid WorkOrderId, string WorkOrderCode, string WorkOrderStatus, string ProductCode, string ProductName, string? ProductRevision,
    string? CustomerName, ManufacturerResponse Manufacturer, List<MachineTestResponse> Tests, List<TechnicalFileItemResponse> TechnicalFile,
    bool TechnicalFileComplete, MachineDeclarationResponse Declaration, List<string> MissingForDeclaration);
