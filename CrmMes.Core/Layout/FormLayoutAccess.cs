namespace CrmMes.Core.Layout;

/// <summary>Chi può cambiare i layout dei moduli. L'Admin lo può sempre; gli altri ruoli solo se l'Admin li
/// ha autorizzati nel profilo aziendale. Leggere i layout resta aperto a tutti gli utenti autenticati.</summary>
public static class FormLayoutAccess
{
    /// <summary>Ruoli che l'Admin può autorizzare. Admin è sempre autorizzato e non compare nell'elenco.</summary>
    public static readonly IReadOnlyList<string> GrantableRoles = ["Management", "Sales", "Purchasing", "Warehouse", "Operator"];

    public static bool CanEdit(string? role, string? grantedRoles) =>
        role == "Admin" || (role is not null && Parse(grantedRoles).Contains(role));

    /// <summary>Ruoli scritti nel campo del profilo, puliti: solo ruoli autorizzabili, senza duplicati.</summary>
    public static IReadOnlyList<string> Parse(string? csv) =>
        (csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(GrantableRoles.Contains)
            .Distinct()
            .ToList();

    public static string Format(IEnumerable<string> roles) =>
        string.Join(",", roles.Where(GrantableRoles.Contains).Distinct());
}
