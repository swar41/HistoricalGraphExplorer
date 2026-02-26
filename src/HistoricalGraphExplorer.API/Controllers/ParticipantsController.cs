using HistoricalGraphExplorer.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace HistoricalGraphExplorer.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ParticipantsController : ControllerBase
{
    private readonly IGraphTraversalService _traversal;

    public ParticipantsController(IGraphTraversalService traversal) => _traversal = traversal;

    /// <summary>
    /// Get all events a participant was involved in.
    /// Pass the slug (e.g. "ottoman-empire") or the full id ("participant:ottoman-empire").
    /// Example: GET /api/participants/ottoman-empire/events
    /// </summary>
    [HttpGet("{slug}/events")]
    public async Task<IActionResult> GetEvents(string slug)
    {
        var result = await _traversal.GetEventsByParticipantAsync(slug);
        return Ok(result);
    }
}
