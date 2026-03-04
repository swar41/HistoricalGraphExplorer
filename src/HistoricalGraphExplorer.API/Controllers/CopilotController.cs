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
    /// Provide a stable sessionId to enable multi-turn conversational memory.
    /// Example: POST /api/copilot/ask
    /// { "sessionId": "abc123", "question": "Which wars involved the Ottoman Empire?" }
    /// Follow-up: { "sessionId": "abc123", "question": "Which of them were after 1700?" }
    /// </summary>
    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AskRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Question))
            return BadRequest(new { error = "Question must not be empty." });

        var answer = await _copilot.AskAsync(req.Question, req.SessionId);
        return Ok(new { sessionId = req.SessionId, question = req.Question, answer });
    }

    /// <summary>
    /// Clear the conversation memory for a session.
    /// Example: DELETE /api/copilot/session/abc123
    /// </summary>
    [HttpDelete("session/{sessionId}")]
    public IActionResult ClearSession(string sessionId,
        [FromServices] IConversationStore store)
    {
        store.Clear(sessionId);
        return Ok(new { cleared = sessionId });
    }
}

public record AskRequest(string Question, string? SessionId = null);
