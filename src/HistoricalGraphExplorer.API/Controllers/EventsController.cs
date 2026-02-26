using HistoricalGraphExplorer.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace HistoricalGraphExplorer.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EventsController : ControllerBase
{
    private readonly IGraphTraversalService _traversal;

    public EventsController(IGraphTraversalService traversal) => _traversal = traversal;

    /// <summary>
    /// List events, optionally filtered by year range.
    /// Example: GET /api/events?fromYear=1600&amp;toYear=1700&amp;limit=50
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? fromYear = null,
        [FromQuery] int? toYear = null,
        [FromQuery] int limit = 100)
    {
        var result = await _traversal.GetEventsByYearRangeAsync(fromYear, toYear, limit);
        return Ok(result);
    }

    /// <summary>
    /// Get full details (event + participants + places) for one event.
    /// Example: GET /api/events/wd:Q74623
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetDetails(string id)
    {
        var result = await _traversal.GetEventDetailsAsync(id);
        return Ok(result);
    }

    /// <summary>
    /// Get graph neighbors of any vertex (Event, Participant, or Place) by its id.
    /// Example: GET /api/events/wd:Q74623/neighbors?depth=1
    /// </summary>
    [HttpGet("{id}/neighbors")]
    public async Task<IActionResult> GetNeighbors(string id, [FromQuery] int depth = 1)
    {
        var result = await _traversal.GetNeighborsAsync(id, depth);
        return Ok(result);
    }
}
