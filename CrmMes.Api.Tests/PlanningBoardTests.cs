using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

public class PlanningBoardTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public PlanningBoardTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task<PlanningCategoryResponse> CreateCategoryAsync(string? colorHex = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var response = await _adminClient.PostAsJsonAsync(
            "/api/planning-board/categories", new CreateCategoryRequest($"C{suffix}"[..7], $"Categoria {suffix}", colorHex));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PlanningCategoryResponse>())!;
    }

    private async Task<PlanningProjectResponse> CreateProjectAsync(string? status = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var response = await _adminClient.PostAsJsonAsync(
            "/api/planning-board/projects", new CreateProjectRequest($"Macchina {suffix}", status, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PlanningProjectResponse>())!;
    }

    [Fact]
    public async Task CreateCategory_AppearsInList_WithSequentialOrder()
    {
        var first = await CreateCategoryAsync();
        var second = await CreateCategoryAsync();

        var list = await _adminClient.GetFromJsonAsync<List<PlanningCategoryResponse>>("/api/planning-board/categories");

        Assert.Contains(list!, c => c.Id == first.Id);
        Assert.Contains(list!, c => c.Id == second.Id);
        var firstIndex = list!.FindIndex(c => c.Id == first.Id);
        var secondIndex = list.FindIndex(c => c.Id == second.Id);
        Assert.True(firstIndex < secondIndex);
    }

    [Fact]
    public async Task CreateCategory_WithDuplicateCode_ReturnsConflict()
    {
        var category = await CreateCategoryAsync();

        var response = await _adminClient.PostAsJsonAsync(
            "/api/planning-board/categories", new CreateCategoryRequest(category.Code, "Altro nome", null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateCategory_IsExcludedFromDefaultList_ButVisibleWithIncludeInactive()
    {
        var category = await CreateCategoryAsync();

        var deleteResponse = await _adminClient.DeleteAsync($"/api/planning-board/categories/{category.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var activeList = await _adminClient.GetFromJsonAsync<List<PlanningCategoryResponse>>("/api/planning-board/categories");
        Assert.DoesNotContain(activeList!, c => c.Id == category.Id);

        var fullList = await _adminClient.GetFromJsonAsync<List<PlanningCategoryResponse>>("/api/planning-board/categories?includeInactive=true");
        Assert.Contains(fullList!, c => c.Id == category.Id && !c.IsActive);
    }

    [Fact]
    public async Task NonAdmin_CannotCreateCategoryOrProject_ButCanView()
    {
        var operatorAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "planning-op");
        using var operatorClient = _fixture.Factory.AuthenticatedClient(operatorAuth.Token);

        var createCategoryResponse = await operatorClient.PostAsJsonAsync(
            "/api/planning-board/categories", new CreateCategoryRequest("X", "Non permesso", null));
        Assert.Equal(HttpStatusCode.Forbidden, createCategoryResponse.StatusCode);

        var createProjectResponse = await operatorClient.PostAsJsonAsync(
            "/api/planning-board/projects", new CreateProjectRequest("Non permesso", null, null, null));
        Assert.Equal(HttpStatusCode.Forbidden, createProjectResponse.StatusCode);

        var viewResponse = await operatorClient.GetAsync("/api/planning-board/categories");
        Assert.Equal(HttpStatusCode.OK, viewResponse.StatusCode);
    }

    [Fact]
    public async Task CreateProject_DefaultsToInValutazione_AndCanBeEditedWithNewStatus()
    {
        var project = await CreateProjectAsync();
        Assert.Equal("InValutazione", project.Status);

        var editResponse = await _adminClient.PutAsJsonAsync(
            $"/api/planning-board/projects/{project.Id}", new EditProjectRequest(project.Name, "Confermata", "nota di prova", null));
        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);
        var updated = (await editResponse.Content.ReadFromJsonAsync<PlanningProjectResponse>())!;
        Assert.Equal("Confermata", updated.Status);
        Assert.Equal("nota di prova", updated.Notes);
    }

    [Fact]
    public async Task PaintCells_IsIdempotentAndVisibleInRange_ThenClearRemovesIt()
    {
        var project = await CreateProjectAsync();
        var category = await CreateCategoryAsync();

        var monday = StartOfWeek(DateTime.UtcNow);
        var weeks = new List<DateTime> { monday, monday.AddDays(7), monday.AddDays(14) };

        var paintResponse = await _adminClient.PutAsJsonAsync(
            "/api/planning-board/cells", new PaintCellsRequest(project.Id, category.Id, weeks));
        Assert.Equal(HttpStatusCode.NoContent, paintResponse.StatusCode);

        // Painting the same weeks again must not throw on the unique index (upsert semantics).
        var repaintResponse = await _adminClient.PutAsJsonAsync(
            "/api/planning-board/cells", new PaintCellsRequest(project.Id, category.Id, weeks));
        Assert.Equal(HttpStatusCode.NoContent, repaintResponse.StatusCode);

        var cells = await _adminClient.GetFromJsonAsync<List<PlanningCellResponse>>(
            $"/api/planning-board/cells?from={monday:yyyy-MM-dd}&weeks=4");
        var projectCells = cells!.Where(c => c.ProjectId == project.Id).ToList();
        Assert.Equal(3, projectCells.Count);
        Assert.All(projectCells, c => Assert.Equal(category.Id, c.CategoryId));

        var clearResponse = await _adminClient.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/planning-board/cells")
        {
            Content = JsonContent.Create(new ClearCellsRequest(project.Id, [monday]))
        });
        Assert.Equal(HttpStatusCode.NoContent, clearResponse.StatusCode);

        var afterClear = await _adminClient.GetFromJsonAsync<List<PlanningCellResponse>>(
            $"/api/planning-board/cells?from={monday:yyyy-MM-dd}&weeks=4");
        Assert.DoesNotContain(afterClear!, c => c.ProjectId == project.Id && c.WeekStart == monday);
        Assert.Contains(afterClear!, c => c.ProjectId == project.Id && c.WeekStart == monday.AddDays(7));
    }

    [Fact]
    public async Task PaintCells_TwoOverlappingCategoriesOnSameWeek_BothAppear()
    {
        var project = await CreateProjectAsync();
        var categoryA = await CreateCategoryAsync();
        var categoryB = await CreateCategoryAsync();
        var monday = StartOfWeek(DateTime.UtcNow);

        await _adminClient.PutAsJsonAsync("/api/planning-board/cells", new PaintCellsRequest(project.Id, categoryA.Id, [monday]));
        await _adminClient.PutAsJsonAsync("/api/planning-board/cells", new PaintCellsRequest(project.Id, categoryB.Id, [monday]));

        var cells = await _adminClient.GetFromJsonAsync<List<PlanningCellResponse>>(
            $"/api/planning-board/cells?from={monday:yyyy-MM-dd}&weeks=1");
        var projectCells = cells!.Where(c => c.ProjectId == project.Id && c.WeekStart == monday).ToList();

        Assert.Equal(2, projectCells.Count);
        Assert.Contains(projectCells, c => c.CategoryId == categoryA.Id);
        Assert.Contains(projectCells, c => c.CategoryId == categoryB.Id);
    }

    [Fact]
    public async Task DeactivatedProject_CellsAreExcludedFromRange()
    {
        var project = await CreateProjectAsync();
        var category = await CreateCategoryAsync();
        var monday = StartOfWeek(DateTime.UtcNow);

        await _adminClient.PutAsJsonAsync("/api/planning-board/cells", new PaintCellsRequest(project.Id, category.Id, [monday]));
        await _adminClient.DeleteAsync($"/api/planning-board/projects/{project.Id}");

        var cells = await _adminClient.GetFromJsonAsync<List<PlanningCellResponse>>(
            $"/api/planning-board/cells?from={monday:yyyy-MM-dd}&weeks=1");
        Assert.DoesNotContain(cells!, c => c.ProjectId == project.Id);
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return DateTime.SpecifyKind(date.Date.AddDays(-diff), DateTimeKind.Utc);
    }
}
