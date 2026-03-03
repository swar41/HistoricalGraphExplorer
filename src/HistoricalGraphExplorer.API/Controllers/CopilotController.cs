using HistoricalGraphExplorer.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace HistoricalGraphExplorer.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CopilotController : ControllerBase
{
    private readonly IGraphCopilotService _copilot;

    public CopilotController(IGraphCopilotService copilot) => _copilot = copilot;

    /// <summary>
    /// Ask a natural-language question about the historical graph.
    /// Example: POST /api/copilot/ask  { "question": "Which wars involved the Ottoman Empire?" }
    /// </summary>
    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AskRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Question))
            return BadRequest(new { error = "Question must not be empty." });

        var answer = await _copilot.AskAsync(req.Question);
        return Ok(new { question = req.Question, answer });
    }
}

public record AskRequest(string Question);
