using CrmMes.Core.Data;
using Microsoft.AspNetCore.Mvc;

namespace CrmMes.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public HealthController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        try
        {
            var connected = await _dbContext.Database.CanConnectAsync();
            return Ok(new { status = connected ? "healthy" : "unavailable", database = "postgres" });
        }
        catch (Exception ex)
        {
            return StatusCode(503, new { status = "error", detail = ex.Message });
        }
    }
}
