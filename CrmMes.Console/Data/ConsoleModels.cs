namespace CrmMes.Console.Data;

/// <summary>A person of the vendor's staff who uses the console. Two-factor is compulsory for everyone.</summary>
public class ConsoleUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? TotpSecret { get; set; }
    public string? TotpPendingSecret { get; set; }
    public long TotpLastStep { get; set; }
    public bool IsActive { get; set; } = true;
    public int FailedLogins { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A company that subscribes: its plan, modules and users, how it pays and until when it has paid.</summary>
public class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? VatNumber { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }

    /// <summary>"stripe" (monthly card or SEPA direct debit, payments recorded at once) or "transfer" (bank
    /// transfer, recorded by hand).</summary>
    public string BillingMethod { get; set; } = BillingMethods.Stripe;
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }

    public string PlanKey { get; set; } = "base";

    /// <summary>Paid modules, comma-separated keys (see ModuleCatalog).</summary>
    public string Modules { get; set; } = string.Empty;

    /// <summary>Users beyond those included in the plan.</summary>
    public int ExtraUsers { get; set; }

    public decimal MonthlyTotal { get; set; }

    /// <summary>Paid through this date (end of the last paid month). Null: not paid yet.</summary>
    public DateTime? PaidUntil { get; set; }

    /// <summary>Days of tolerance after PaidUntil before the service is suspended.</summary>
    public int GraceDays { get; set; } = 15;

    /// <summary>Manual decision that overrides the payments: "active" or "suspended"; null follows the rules.</summary>
    public string? ForcedStatus { get; set; }

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Installation> Installations { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
}

public static class BillingMethods
{
    public const string Stripe = "stripe";
    public const string Transfer = "transfer";
}

/// <summary>One running copy of the management software (cloud or on a customer's server), identified by
/// its license key (kept only as a hash), with what it last reported.</summary>
public class Installation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public bool Revoked { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastHeartbeatAt { get; set; }
    public string? Version { get; set; }
    public string? Hosting { get; set; }
    public string? CompanyName { get; set; }
    public string? VatNumber { get; set; }
    public int? ActiveUsers { get; set; }
    public long? DatabaseSizeBytes { get; set; }
    public string? EnabledModules { get; set; }
    public bool? DatabaseOk { get; set; }
    public string? LastStatusSent { get; set; }
    public string? LastIpAddress { get; set; }
}

public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;
    public string Method { get; set; } = BillingMethods.Transfer;
    public string? Reference { get; set; }
    public string? StripeInvoiceId { get; set; }

    /// <summary>"paid" or "failed" (a Stripe attempt that didn't go through).</summary>
    public string Status { get; set; } = "paid";
    public DateTime? PeriodEnd { get; set; }
    public string? RecordedBy { get; set; }
}

/// <summary>Price list: plans (with the users they include), the price of each module and of an extra user.</summary>
public class PriceItem
{
    public string Key { get; set; } = string.Empty;

    /// <summary>"plan", "module" or "user".</summary>
    public string Kind { get; set; } = "module";
    public string Name { get; set; } = string.Empty;
    public decimal MonthlyPrice { get; set; }
    public int? IncludedUsers { get; set; }
    public string? StripePriceId { get; set; }
    public bool Active { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>A request for help sent from an installation, with the server's state at that moment.</summary>
public class SupportTicket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Number { get; set; }
    public Guid InstallationId { get; set; }
    public Installation Installation { get; set; } = null!;
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string? Contact { get; set; }
    public string? RemoteSessionId { get; set; }
    public string? DiagnosticsJson { get; set; }

    /// <summary>"open", "in-progress" or "closed" (see SupportTicketStatus).</summary>
    public string Status { get; set; } = "open";
    public string? Reply { get; set; }
    public DateTime? RepliedAt { get; set; }
    public string? RepliedBy { get; set; }
}

public class ConsoleSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class ConsoleAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string User { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}
