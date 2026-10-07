using CrmMes.Api.Services;
using CrmMes.Core.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmMes.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/assistant")]
public class AssistantController : ControllerBase
{
    private readonly IAiAssistant? _assistant;
    private readonly IConfiguration _configuration;
    private readonly ApplicationDbContext _dbContext;

    public AssistantController(IConfiguration configuration, ApplicationDbContext dbContext, IAiAssistant? assistant = null)
    {
        _configuration = configuration;
        _dbContext = dbContext;
        _assistant = assistant;
    }

    [HttpPost("ask")]
    public async Task<ActionResult<AiAssistantResponse>> Ask(AssistantAskRequest request, CancellationToken cancellationToken = default)
    {
        var provider = (_configuration["Ai:Provider"] ?? "off").Trim().ToLowerInvariant();
        if (provider is "off" or "" || _assistant is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Assistente IA non attivo: imposta Ai:Provider su stub o openai." });
        }

        var question = request.Question?.Trim();
        if (string.IsNullOrWhiteSpace(question))
        {
            return BadRequest(new { message = "La domanda è obbligatoria." });
        }

        try
        {
            var result = await _assistant.AskAsync(question, cancellationToken);
            AuditTrail.Add(_dbContext, User, "AssistantAsked", "Assistant", null,
                $"Domanda all'assistente IA: {question[..Math.Min(question.Length, 300)]}");
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Servizio IA non raggiungibile." });
        }
    }
}

public sealed record AssistantAskRequest(string? Question);
