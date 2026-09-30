using System.Globalization;
using CrmMes.Console.Data;
using CrmMes.Console.Services;
using CrmMes.Licensing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Console.Pages;

public static class Fmt
{
    public static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");
    public static string Money(decimal value) => value.ToString("C", Italian);
    public static string Date(DateTime? value) => value is null ? "—" : value.Value.ToLocalTime().ToString("dd/MM/yyyy", Italian);
    public static string DateTime(DateTime? value) => value is null ? "—" : value.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Italian);
    public static string StatusClass(string status) => status switch { LicenseStatus.Active => "ok", LicenseStatus.Grace => "warn", _ => "danger" };
    public static string Size(long? bytes) => bytes is null ? "—" : $"{bytes.Value / 1_048_576d:N0} MB";
}

public class IndexModel(ConsoleDbContext db) : PageModel
{
    public int Customers { get; private set; }
    public int Active { get; private set; }
    public int Late { get; private set; }
    public int Suspended { get; private set; }
    public decimal MonthlyRevenue { get; private set; }
    public int Online { get; private set; }
    public int Offline { get; private set; }
    public int OpenTickets { get; private set; }
    public List<(Customer Customer, string Status, string? Message)> Attention { get; } = [];

    public async Task OnGetAsync()
    {
        var now = System.DateTime.UtcNow;
        var customers = await db.Customers.AsNoTracking().Include(c => c.Installations).ToListAsync();
        Customers = customers.Count;
        foreach (var customer in customers)
        {
            var (status, message) = SubscriptionRules.Evaluate(customer, now);
            switch (status)
            {
                case LicenseStatus.Active: Active++; MonthlyRevenue += customer.MonthlyTotal; break;
                case LicenseStatus.Grace: Late++; MonthlyRevenue += customer.MonthlyTotal; Attention.Add((customer, status, message)); break;
                default: Suspended++; Attention.Add((customer, status, message)); break;
            }
        }

        OpenTickets = await db.Tickets.CountAsync(t => t.Status != SupportTicketStatus.Closed);
        var installations = customers.SelectMany(c => c.Installations).Where(i => !i.Revoked).ToList();
        Online = installations.Count(i => SubscriptionRules.IsOnline(i, now));
        Offline = installations.Count - Online;
    }
}

public class CustomersIndexModel(ConsoleDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    public List<(Customer Customer, string Status, int Installations, int Online)> Rows { get; } = [];

    public async Task OnGetAsync()
    {
        var query = db.Customers.AsNoTracking().Include(c => c.Installations).AsQueryable();
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim().ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(term) || (c.VatNumber != null && c.VatNumber.ToLower().Contains(term)));
        }

        var now = System.DateTime.UtcNow;
        foreach (var customer in await query.OrderBy(c => c.Name).ToListAsync())
        {
            var active = customer.Installations.Where(i => !i.Revoked).ToList();
            Rows.Add((customer, SubscriptionRules.Evaluate(customer, now).Status, active.Count, active.Count(i => SubscriptionRules.IsOnline(i, now))));
        }
    }
}

public class CustomerNewModel(ConsoleDbContext db) : PageModel
{
    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public string? VatNumber { get; set; }
    [BindProperty] public string? Email { get; set; }
    [BindProperty] public string BillingMethod { get; set; } = BillingMethods.Stripe;
    public string? Error { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            Error = "La ragione sociale è obbligatoria.";
            return Page();
        }

        var customer = new Customer
        {
            Name = Name.Trim(),
            VatNumber = string.IsNullOrWhiteSpace(VatNumber) ? null : VatNumber.Trim(),
            Email = string.IsNullOrWhiteSpace(Email) ? null : Email.Trim(),
            BillingMethod = BillingMethod == BillingMethods.Transfer ? BillingMethods.Transfer : BillingMethods.Stripe,
        };
        customer.MonthlyTotal = Pricing.MonthlyTotal(customer, await db.Prices.AsNoTracking().ToListAsync());
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        await ConsoleAuth.AuditAsync(db, User, "CustomerCreated", customer.Name);
        return RedirectToPage("/Customers/Details", new { id = customer.Id });
    }
}

/// <summary>Everything about one customer: data, subscription (plan, modules, users → monthly fee), status
/// and manual override, installations with their license keys, payments.</summary>
public class CustomerDetailsModel(ConsoleDbContext db, StripeOptions stripe, IStripeGateway gateway) : PageModel
{
    public bool StripeEnabled => stripe.Enabled;
    public bool StripeTestMode => stripe.IsTestMode;
    public string? PaymentLink { get; private set; }

    public Customer Customer { get; private set; } = null!;
    public List<PriceItem> Prices { get; private set; } = [];
    public string Status { get; private set; } = LicenseStatus.Active;
    public string? StatusMessage { get; private set; }
    public string? NewKey { get; private set; }
    public string? NewKeyInstallation { get; private set; }
    public string? Info { get; private set; }
    public string? Error { get; private set; }

    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public string? VatNumber { get; set; }
    [BindProperty] public string? Email { get; set; }
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public string BillingMethod { get; set; } = BillingMethods.Stripe;
    [BindProperty] public int GraceDays { get; set; } = 15;
    [BindProperty] public string? Notes { get; set; }
    [BindProperty] public string PlanKey { get; set; } = "base";
    [BindProperty] public List<string> Modules { get; set; } = [];
    [BindProperty] public int ExtraUsers { get; set; }
    [BindProperty] public string? InstallationName { get; set; }
    [BindProperty] public decimal PaymentAmount { get; set; }
    [BindProperty] public DateTime? PaymentDate { get; set; }
    [BindProperty] public DateTime? PaidUntil { get; set; }
    [BindProperty] public string? PaymentReference { get; set; }

    public bool IsSelected(string key) => Pricing.ParseModules(Customer.Modules).Contains(key);

    public async Task<IActionResult> OnGetAsync(Guid id) => await LoadAsync(id) ? Page() : NotFound();

    public async Task<IActionResult> OnPostDataAsync(Guid id)
    {
        if (!await LoadAsync(id, tracking: true))
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            Error = "La ragione sociale è obbligatoria.";
            return Page();
        }

        Customer.Name = Name.Trim();
        Customer.VatNumber = Clean(VatNumber);
        Customer.Email = Clean(Email);
        Customer.Phone = Clean(Phone);
        Customer.BillingMethod = BillingMethod == BillingMethods.Transfer ? BillingMethods.Transfer : BillingMethods.Stripe;
        Customer.GraceDays = Math.Clamp(GraceDays, 0, 90);
        Customer.Notes = Clean(Notes);
        await db.SaveChangesAsync();
        await ConsoleAuth.AuditAsync(db, User, "CustomerSaved", Customer.Name);
        return await ReloadAsync(id, "Dati salvati.");
    }

    /// <summary>New plan, modules or users: the monthly fee is recomputed and the installations receive the
    /// new license at their next hourly check.</summary>
    public async Task<IActionResult> OnPostSubscriptionAsync(Guid id)
    {
        if (!await LoadAsync(id, tracking: true))
        {
            return NotFound();
        }

        if (Prices.All(p => p.Kind != "plan" || p.Key != PlanKey))
        {
            Error = "Piano non valido.";
            return Page();
        }

        var before = Customer.MonthlyTotal;
        Customer.PlanKey = PlanKey;
        Customer.Modules = string.Join(',', Modules.Where(m => ModuleCatalog.All.Any(c => c.Key == m)).Distinct());
        Customer.ExtraUsers = Math.Clamp(ExtraUsers, 0, 1000);
        Customer.MonthlyTotal = Pricing.MonthlyTotal(Customer, Prices);
        await db.SaveChangesAsync();
        await ConsoleAuth.AuditAsync(db, User, "SubscriptionChanged",
            $"{Customer.Name}: piano {PlanKey}, moduli {Customer.Modules}, utenti extra {Customer.ExtraUsers}, canone {Fmt.Money(before)} → {Fmt.Money(Customer.MonthlyTotal)}");

        // Stripe subscription running: the new fee becomes its price at once, with a proportional adjustment.
        var stripeNote = string.Empty;
        if (stripe.Enabled && Customer.StripeSubscriptionId is not null && Customer.MonthlyTotal != before && Customer.MonthlyTotal > 0)
        {
            try
            {
                var price = await gateway.CreateMonthlyPriceAsync(Customer.MonthlyTotal, $"{Customer.Name} - {Customer.PlanKey}", HttpContext.RequestAborted);
                await gateway.ChangeSubscriptionPriceAsync(Customer.StripeSubscriptionId, price, HttpContext.RequestAborted);
                stripeNote = " Canone aggiornato anche su Stripe (con conguaglio proporzionale).";
            }
            catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
            {
                Error = $"Canone salvato qui, ma non su Stripe: {exception.Message}";
            }
        }

        return await ReloadAsync(id, $"Abbonamento aggiornato: canone mensile {Fmt.Money(Customer.MonthlyTotal)}. Le installazioni lo ricevono entro un'ora.{stripeNote}");
    }

    public async Task<IActionResult> OnPostStatusAsync(Guid id, string? forced)
    {
        if (!await LoadAsync(id, tracking: true))
        {
            return NotFound();
        }

        Customer.ForcedStatus = forced is LicenseStatus.Active or LicenseStatus.Suspended ? forced : null;
        await db.SaveChangesAsync();
        await ConsoleAuth.AuditAsync(db, User, "StatusOverride", $"{Customer.Name}: {Customer.ForcedStatus ?? "secondo i pagamenti"}");
        return await ReloadAsync(id, Customer.ForcedStatus switch
        {
            LicenseStatus.Suspended => "Cliente sospeso: entro un'ora le sue installazioni passano alla sola consultazione.",
            LicenseStatus.Active => "Cliente attivo a prescindere dai pagamenti.",
            _ => "Lo stato torna a dipendere dai pagamenti.",
        });
    }

    /// <summary>The Stripe payment link to send to the customer: card or SEPA direct debit, monthly, at the
    /// current fee. Paying it links the subscription (webhook) and every monthly payment is then recorded.</summary>
    public async Task<IActionResult> OnPostStripeCheckoutAsync(Guid id)
    {
        if (!await LoadAsync(id, tracking: true))
        {
            return NotFound();
        }

        if (!stripe.Enabled || Customer.MonthlyTotal <= 0)
        {
            Error = stripe.Enabled ? "Imposta prima piano e moduli: il canone è zero." : "Stripe non è collegato.";
            return Page();
        }

        try
        {
            Customer.StripeCustomerId ??= await gateway.CreateCustomerAsync(Customer.Name, Customer.Email, Customer.Id, HttpContext.RequestAborted);
            await db.SaveChangesAsync();
            var price = await gateway.CreateMonthlyPriceAsync(Customer.MonthlyTotal, $"{Customer.Name} - {Customer.PlanKey}", HttpContext.RequestAborted);
            var back = $"{Request.Scheme}://{Request.Host}/";
            PaymentLink = await gateway.CreateCheckoutAsync(Customer.StripeCustomerId, price, Customer.Id, back + "?pagato=1", back, HttpContext.RequestAborted);
            await ConsoleAuth.AuditAsync(db, User, "StripeCheckoutCreated", Customer.Name);
            Info = "Link di pagamento creato: invialo al cliente. Quando paga, l'abbonamento si collega da solo.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            Error = exception.Message;
        }

        return Page();
    }

    /// <summary>Link for the customer to change card or bank account on Stripe's own page.</summary>
    public async Task<IActionResult> OnPostStripePortalAsync(Guid id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }

        if (!stripe.Enabled || Customer.StripeCustomerId is null)
        {
            Error = "Il cliente non ha ancora un profilo Stripe.";
            return Page();
        }

        try
        {
            PaymentLink = await gateway.CreatePortalAsync(Customer.StripeCustomerId, $"{Request.Scheme}://{Request.Host}/", HttpContext.RequestAborted);
            Info = "Link per aggiornare il metodo di pagamento: invialo al cliente (vale per poco tempo).";
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            Error = exception.Message;
        }

        return Page();
    }

    /// <summary>A new installation and its license key, shown this one time only.</summary>
    public async Task<IActionResult> OnPostInstallationAsync(Guid id)
    {
        if (!await LoadAsync(id, tracking: true))
        {
            return NotFound();
        }

        var key = LicenseKeys.New();
        var name = string.IsNullOrWhiteSpace(InstallationName) ? $"Installazione {Customer.Installations.Count + 1}" : InstallationName.Trim();
        db.Installations.Add(new Installation { CustomerId = Customer.Id, Name = name, KeyHash = LicenseKeys.Hash(key), KeyPrefix = LicenseKeys.Prefix(key) });
        await db.SaveChangesAsync();
        await ConsoleAuth.AuditAsync(db, User, "InstallationCreated", $"{Customer.Name}: {name}");
        await LoadAsync(id);
        NewKey = key;
        NewKeyInstallation = name;
        return Page();
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid id, Guid installationId)
    {
        if (!await LoadAsync(id, tracking: true))
        {
            return NotFound();
        }

        var installation = Customer.Installations.SingleOrDefault(i => i.Id == installationId);
        if (installation is not null)
        {
            installation.Revoked = true;
            await db.SaveChangesAsync();
            await ConsoleAuth.AuditAsync(db, User, "InstallationRevoked", $"{Customer.Name}: {installation.Name}");
        }

        return await ReloadAsync(id, "Chiave revocata: quell'installazione non riceve più licenze e, senza rinnovo, dopo 30 giorni passa alla sola consultazione.");
    }

    /// <summary>A bank transfer received: amount, date and the date it pays through.</summary>
    public async Task<IActionResult> OnPostPaymentAsync(Guid id)
    {
        if (!await LoadAsync(id, tracking: true))
        {
            return NotFound();
        }

        if (PaymentAmount <= 0 || PaidUntil is null)
        {
            Error = "Indica l'importo e la data fino a cui il pagamento copre l'abbonamento.";
            return Page();
        }

        var paidUntil = System.DateTime.SpecifyKind(PaidUntil.Value.Date, DateTimeKind.Utc);
        db.Payments.Add(new Payment
        {
            CustomerId = Customer.Id,
            Amount = PaymentAmount,
            PaidAt = System.DateTime.SpecifyKind((PaymentDate ?? System.DateTime.UtcNow).Date, DateTimeKind.Utc),
            Method = BillingMethods.Transfer,
            Reference = Clean(PaymentReference),
            PeriodEnd = paidUntil,
            RecordedBy = User.Identity?.Name,
        });
        if (Customer.PaidUntil is null || paidUntil > Customer.PaidUntil)
        {
            Customer.PaidUntil = paidUntil;
        }

        await db.SaveChangesAsync();
        await ConsoleAuth.AuditAsync(db, User, "PaymentRecorded", $"{Customer.Name}: {Fmt.Money(PaymentAmount)} fino al {Fmt.Date(paidUntil)}");
        return await ReloadAsync(id, $"Pagamento registrato: in regola fino al {Fmt.Date(Customer.PaidUntil)}.");
    }

    private async Task<IActionResult> ReloadAsync(Guid id, string info)
    {
        await LoadAsync(id);
        Info = info;
        return Page();
    }

    private async Task<bool> LoadAsync(Guid id, bool tracking = false)
    {
        var query = db.Customers.Include(c => c.Installations).Include(c => c.Payments).AsQueryable();
        var customer = await (tracking ? query : query.AsNoTracking()).SingleOrDefaultAsync(c => c.Id == id);
        if (customer is null)
        {
            return false;
        }

        Customer = customer;
        Prices = await db.Prices.AsNoTracking().OrderBy(p => p.SortOrder).ToListAsync();
        (Status, StatusMessage) = SubscriptionRules.Evaluate(customer, System.DateTime.UtcNow);
        if (!tracking || Request.Method == "GET")
        {
            Name = customer.Name;
            VatNumber = customer.VatNumber;
            Email = customer.Email;
            Phone = customer.Phone;
            BillingMethod = customer.BillingMethod;
            GraceDays = customer.GraceDays;
            Notes = customer.Notes;
            PlanKey = customer.PlanKey;
            ExtraUsers = customer.ExtraUsers;
        }

        return true;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public class TicketsModel(ConsoleDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string Show { get; set; } = "open";
    public List<SupportTicket> Rows { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var query = db.Tickets.AsNoTracking().Include(t => t.Customer).Include(t => t.Installation).AsQueryable();
        if (Show != "all")
        {
            query = query.Where(t => t.Status != SupportTicketStatus.Closed);
        }

        Rows = await query.OrderByDescending(t => t.CreatedAt).Take(300).ToListAsync();
    }

    public static string StatusName(string status) => status switch
    {
        SupportTicketStatus.Open => "Aperta",
        SupportTicketStatus.InProgress => "In lavorazione",
        SupportTicketStatus.Closed => "Chiusa",
        _ => status,
    };

    public static string StatusClass(string status) => status switch
    {
        SupportTicketStatus.Open => "danger",
        SupportTicketStatus.InProgress => "warn",
        _ => "ok",
    };
}

/// <summary>One request: what the customer wrote, the server's state attached, the answer (which the customer
/// sees in the management software) and the state.</summary>
public class TicketModel(ConsoleDbContext db) : PageModel
{
    public SupportTicket Ticket { get; private set; } = null!;
    public string? DiagnosticsPretty { get; private set; }
    public string? Info { get; private set; }

    [BindProperty] public string? Reply { get; set; }
    [BindProperty] public string Status { get; set; } = SupportTicketStatus.InProgress;

    public async Task<IActionResult> OnGetAsync(Guid id) => await LoadAsync(id) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(Guid id)
    {
        var ticket = await db.Tickets.SingleOrDefaultAsync(t => t.Id == id);
        if (ticket is null)
        {
            return NotFound();
        }

        if (!string.IsNullOrWhiteSpace(Reply) && Reply.Trim() != ticket.Reply)
        {
            ticket.Reply = Reply.Trim()[..Math.Min(Reply.Trim().Length, 4000)];
            ticket.RepliedAt = System.DateTime.UtcNow;
            ticket.RepliedBy = User.Identity?.Name;
        }

        ticket.Status = Status is SupportTicketStatus.Open or SupportTicketStatus.InProgress or SupportTicketStatus.Closed ? Status : ticket.Status;
        await db.SaveChangesAsync();
        await ConsoleAuth.AuditAsync(db, User, "TicketUpdated", $"Richiesta {ticket.Number}: {ticket.Status}");
        await LoadAsync(id);
        Info = "Salvato: il cliente vede risposta e stato nel gestionale.";
        return Page();
    }

    private async Task<bool> LoadAsync(Guid id)
    {
        var ticket = await db.Tickets.AsNoTracking().Include(t => t.Customer).Include(t => t.Installation).SingleOrDefaultAsync(t => t.Id == id);
        if (ticket is null)
        {
            return false;
        }

        Ticket = ticket;
        Reply = ticket.Reply;
        Status = ticket.Status;
        if (ticket.DiagnosticsJson is not null)
        {
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(ticket.DiagnosticsJson);
                DiagnosticsPretty = System.Text.Json.JsonSerializer.Serialize(document.RootElement, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            catch (System.Text.Json.JsonException)
            {
                DiagnosticsPretty = ticket.DiagnosticsJson;
            }
        }

        return true;
    }
}

public class InstallationsModel(ConsoleDbContext db) : PageModel
{
    public List<Installation> Rows { get; private set; } = [];

    public async Task OnGetAsync() =>
        Rows = await db.Installations.AsNoTracking().Include(i => i.Customer).Where(i => !i.Revoked)
            .OrderBy(i => i.Customer.Name).ThenBy(i => i.Name).ToListAsync();
}

public class PaymentsModel(ConsoleDbContext db) : PageModel
{
    public List<Payment> Rows { get; private set; } = [];

    public async Task OnGetAsync() =>
        Rows = await db.Payments.AsNoTracking().Include(p => p.Customer).OrderByDescending(p => p.PaidAt).Take(300).ToListAsync();
}

/// <summary>Price list. A change applies to new subscription changes; existing monthly fees are recomputed
/// only when "ricalcola i canoni" is chosen, so no customer's fee changes by surprise.</summary>
public class PricesModel(ConsoleDbContext db) : PageModel
{
    public List<PriceItem> Rows { get; private set; } = [];
    public string? Info { get; private set; }

    [BindProperty] public Dictionary<string, decimal> Price { get; set; } = [];
    [BindProperty] public Dictionary<string, int?> Users { get; set; } = [];
    [BindProperty] public bool Recompute { get; set; }

    public async Task OnGetAsync() => Rows = await db.Prices.OrderBy(p => p.SortOrder).ToListAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        Rows = await db.Prices.OrderBy(p => p.SortOrder).ToListAsync();
        foreach (var item in Rows)
        {
            if (Price.TryGetValue(item.Key, out var price) && price >= 0)
            {
                item.MonthlyPrice = price;
            }

            if (item.Kind == "plan" && Users.TryGetValue(item.Key, out var users) && users is > 0)
            {
                item.IncludedUsers = users;
            }
        }

        var recomputed = 0;
        if (Recompute)
        {
            foreach (var customer in await db.Customers.ToListAsync())
            {
                customer.MonthlyTotal = Pricing.MonthlyTotal(customer, Rows);
                recomputed++;
            }
        }

        await db.SaveChangesAsync();
        await ConsoleAuth.AuditAsync(db, User, "PricesSaved", Recompute ? $"Listino salvato, {recomputed} canoni ricalcolati" : "Listino salvato");
        Info = Recompute ? $"Listino salvato e {recomputed} canoni ricalcolati." : "Listino salvato. I canoni esistenti restano invariati.";
        return Page();
    }
}
