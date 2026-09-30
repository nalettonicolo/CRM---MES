namespace CrmMes.Api.Services;

/// <summary>The 14 allergens of Reg. (UE) 1169/2011, Annex II, which must be emphasised in the list of
/// ingredients on a food label. Keys are stored on materials; names are what gets printed.</summary>
public static class FoodAllergens
{
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        ["gluten"] = "Cereali contenenti glutine",
        ["crustaceans"] = "Crostacei",
        ["eggs"] = "Uova",
        ["fish"] = "Pesce",
        ["peanuts"] = "Arachidi",
        ["soy"] = "Soia",
        ["milk"] = "Latte",
        ["nuts"] = "Frutta a guscio",
        ["celery"] = "Sedano",
        ["mustard"] = "Senape",
        ["sesame"] = "Semi di sesamo",
        ["sulphites"] = "Anidride solforosa e solfiti",
        ["lupin"] = "Lupini",
        ["molluscs"] = "Molluschi",
    };

    public static bool IsKnown(string key) => Names.ContainsKey(key);

    /// <summary>Normalized, known, de-duplicated keys in the Annex II order.</summary>
    public static List<string> Parse(string? stored) =>
        Normalize((stored ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public static List<string> Normalize(IEnumerable<string> keys)
    {
        var set = keys.Select(key => key.Trim().ToLowerInvariant()).ToHashSet();
        return Names.Keys.Where(set.Contains).ToList();
    }
}
