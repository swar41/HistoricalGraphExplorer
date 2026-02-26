using HistoricalGraphExplorer.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace HistoricalGraphExplorer.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PlacesController : ControllerBase
{
    private readonly IGraphTraversalService _traversal;

    public PlacesController(IGraphTraversalService traversal) => _traversal = traversal;

    /// <summary>
    /// Get all events that occurred at a place.
    /// Pass the slug (e.g. "ukraine") or full id ("place:ukraine").
    /// Example: GET /api/places/ukraine/events
    /// </summary>
    [HttpGet("{slug}/events")]
    public async Task<IActionResult> GetEvents(string slug)
    {
        var result = await _traversal.GetEventsByPlaceAsync(slug);
        return Ok(result);
    }
}
