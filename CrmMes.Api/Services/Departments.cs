namespace CrmMes.Api.Services;

/// <summary>The departments a manufacturing company is made of. A company is rarely one trade: a machine
/// builder machines parts, assembles, wires the panels, tests and services the machines at the customer's.
/// So the configuration asks first what the company does (one or more activities, see <see cref="Sectors"/>),
/// then which departments it has (each one pre-ticked by the activities, renamable, repeatable), and only
/// then which modules to switch on: each department proposes the modules it needs and its typical work
/// centers, so a department chosen is a department ready to use.
///
/// A department becomes an Area with its type; its work centers point to it. That link is what lets the
/// shop-floor terminal and the phone page show each operator the work of their own department first.</summary>
public static class Departments
{
    public sealed record WorkCenterTemplate(string Code, string Name);

    public sealed record DepartmentInfo(
        string Key,
        string Name,
        string Description,
        IReadOnlyList<string> Modules,
        IReadOnlyList<WorkCenterTemplate> WorkCenters,
        IReadOnlyList<string> Activities);

    public static readonly IReadOnlyList<DepartmentInfo> All =
    [
        new("sales", "Ufficio commerciale", "Offerte, clienti, ordini e fatture.",
            ["sales", "invoicing"], [], ["*"]),
        new("engineering", "Ufficio tecnico", "Progetto, distinte e cicli, disegni e schemi, modifiche tecniche.",
            ["engineering"], [], ["machine-building", "electrical-panels", "mechanical"]),
        new("purchasing", "Acquisti", "Fornitori, ordini, listini e conto lavoro.",
            ["purchasing"], [], ["*"]),
        new("warehouse", "Magazzino", "Ricevimento merce, lotti, prelievi per le commesse.",
            [], [], ["*"]),
        new("machining", "Produzione meccanica", "Lavorazioni alle macchine utensili: tornitura, fresatura, centri di lavoro.",
            ["shopfloor", "maintenance", "quality"],
            [new("CNC", "Centro di lavoro CNC"), new("TORNIO", "Tornio"), new("FRESA", "Fresatrice")],
            ["machine-building", "mechanical"]),
        new("fabrication", "Carpenteria e saldatura", "Taglio, piegatura, saldatura di lamiere e profilati.",
            ["shopfloor", "subcontracting", "maintenance"],
            [new("TAGLIO", "Taglio laser e plasma"), new("PIEGA", "Piegatura"), new("SALD", "Saldatura")],
            ["mechanical"]),
        new("assembly", "Assemblaggio meccanico", "Montaggio di gruppi e macchine.",
            ["shopfloor"],
            [new("MONT", "Banco montaggio")],
            ["machine-building"]),
        new("panels", "Reparto quadristi", "Cablaggio di quadri elettrici e bordo macchina.",
            ["shopfloor", "panel-verification"],
            [new("CABL", "Banco cablaggio")],
            ["machine-building", "electrical-panels"]),
        new("testing", "Collaudo", "Collaudi e verifiche finali, certificati e dichiarazioni.",
            ["quality", "machine-testing"],
            [new("COLL", "Sala collaudo")],
            ["machine-building", "electrical-panels"]),
        new("service", "Service e assistenza", "Assistenza presso i clienti su macchine e quadri installati, garanzie.",
            ["service", "site-work"],
            [new("SERV", "Assistenza esterna")],
            ["machine-building", "electrical-panels"]),
        new("site", "Cantiere e installazioni", "Lavori presso il cliente con rapportini firmati.",
            ["site-work"],
            [new("CANT", "Squadra di cantiere")],
            ["installations"]),
        new("food-production", "Produzione alimentare", "Lavorazione, confezionamento, controlli HACCP.",
            ["shopfloor", "lot-expiry", "food-labels", "haccp"],
            [new("LINEA", "Linea di produzione"), new("CONF", "Confezionamento")],
            ["food"]),
        new("shipping", "Spedizioni", "Imballo, documenti di trasporto, corrieri.",
            ["shipping"], [], ["*"]),
    ];

    public static DepartmentInfo? Find(string? key) =>
        All.FirstOrDefault(department => string.Equals(department.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Departments a new company with these activities most likely has (the Admin adjusts).</summary>
    public static List<string> SuggestedFor(IEnumerable<string> activities)
    {
        var chosen = activities.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return All
            .Where(d => d.Activities.Contains("*") || d.Activities.Any(chosen.Contains))
            .Select(d => d.Key)
            .ToList();
    }

    /// <summary>Modules proposed by activities and departments together: what each one needs, restricted to
    /// modules that exist and are available.</summary>
    public static List<string> SuggestedModules(IEnumerable<string> activities, IEnumerable<string> departments)
    {
        var fromActivities = activities.Select(Sectors.Find).Where(s => s is not null).SelectMany(s => s!.Modules);
        var fromDepartments = departments.Select(Find).Where(d => d is not null).SelectMany(d => d!.Modules);
        return Sectors.Modules
            .Where(m => m.Available)
            .Select(m => m.Key)
            .Where(key => fromActivities.Concat(fromDepartments).Contains(key, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }
}
