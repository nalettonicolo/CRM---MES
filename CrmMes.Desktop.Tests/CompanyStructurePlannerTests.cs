namespace CrmMes.Desktop.Tests;

public class CompanyStructurePlannerTests
{
    private static readonly SectorDto MachineBuilding = new("machine-building", "Costruzione macchine", "",
        ["sales", "shopfloor", "engineering"], ["machining", "panels", "testing", "service"]);
    private static readonly SectorDto Installations = new("installations", "Impiantistica", "", ["site-work"], ["site"]);

    private static readonly List<DepartmentCatalogDto> Catalog =
    [
        new("panels", "Reparto quadristi", "", ["shopfloor", "panel-verification"], [new("CABL", "Banco cablaggio")]),
        new("service", "Service e assistenza", "", ["service", "site-work"], []),
    ];

    private static readonly List<ModuleDto> Modules =
    [
        new("sales", "Vendite", "", false), new("shopfloor", "Terminale", "", false), new("site-work", "Cantiere", "", true),
        new("panel-verification", "61439", "", true), new("engineering", "Ufficio tecnico", "", true, Available: false),
        new("service", "Service", "", true, Available: false),
    ];

    [Fact]
    public void Activities_ProposeTheirDepartments_Together()
    {
        var departments = CompanyStructurePlanner.SuggestedDepartments([MachineBuilding, Installations]);

        Assert.Equal(["machining", "panels", "testing", "service", "site"], departments.ToList());
    }

    [Fact]
    public void Modules_ComeWithTheirReasons_AndOnlyWhenAvailable()
    {
        var reasons = CompanyStructurePlanner.SuggestedModules(
            [MachineBuilding], [("panels", "Quadristi"), ("service", "Assistenza")], Catalog, Modules);

        Assert.Equal(["Costruzione macchine", "Quadristi"], reasons["shopfloor"]);
        Assert.Equal(["Quadristi"], reasons["panel-verification"]);
        Assert.Equal(["Assistenza"], reasons["site-work"]);
        Assert.False(reasons.ContainsKey("engineering"));   // announced, not available yet
        Assert.False(reasons.ContainsKey("service"));
    }

    [Fact]
    public void SecondDepartmentOfAKind_GetsAFreeName() =>
        Assert.Equal("Collaudo 3", CompanyStructurePlanner.UniqueName("Collaudo", ["Collaudo", "collaudo 2", "Officina"]));
}
