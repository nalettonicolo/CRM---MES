namespace CrmMes.Desktop;

/// <summary>What the configuration proposes from what the Admin has chosen so far (the same rules as
/// Departments in CrmMes.Api): the activities propose departments, activities and departments together
/// propose modules, each with the reasons shown next to it ("proposto da: Quadristi").</summary>
public static class CompanyStructurePlanner
{
    public static HashSet<string> SuggestedDepartments(IEnumerable<SectorDto> activities) =>
        activities.SelectMany(activity => activity.Departments ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Module key → who proposes it (activity names, department names), for available modules only.</summary>
    public static Dictionary<string, List<string>> SuggestedModules(
        IEnumerable<SectorDto> activities,
        IEnumerable<(string Type, string Name)> departments,
        IReadOnlyList<DepartmentCatalogDto> departmentCatalog,
        IReadOnlyList<ModuleDto> modules)
    {
        var available = modules.Where(m => m.Available).Select(m => m.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reasons = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        void Add(string module, string reason)
        {
            if (!available.Contains(module))
            {
                return;
            }

            if (!reasons.TryGetValue(module, out var list))
            {
                reasons[module] = list = [];
            }

            if (!list.Contains(reason))
            {
                list.Add(reason);
            }
        }

        foreach (var activity in activities)
        {
            foreach (var module in activity.Modules)
            {
                Add(module, activity.Name);
            }
        }

        foreach (var (type, name) in departments)
        {
            var info = departmentCatalog.FirstOrDefault(d => string.Equals(d.Key, type, StringComparison.OrdinalIgnoreCase));
            foreach (var module in info?.Modules ?? [])
            {
                Add(module, name);
            }
        }

        return reasons;
    }

    /// <summary>A name not used yet by another department: "Collaudo", then "Collaudo 2"...</summary>
    public static string UniqueName(string baseName, IEnumerable<string> taken)
    {
        var names = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(baseName))
        {
            return baseName;
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
