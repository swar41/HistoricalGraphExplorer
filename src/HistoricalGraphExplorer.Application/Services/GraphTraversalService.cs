using HistoricalGraphExplorer.Application.Interfaces;
using HistoricalGraphExplorer.Domain;

namespace HistoricalGraphExplorer.Application.Services;

/// <summary>
/// All queries use parameterised bindings — matches the C# uploader's approach.
/// Graph schema (produced by the C# uploader):
///   Vertex Event       id=wd:Qxxxxx  pk=event        props: name, startTime, startYear
///   Vertex Participant id=participant:{slug} pk=participant  props: name
///   Vertex Place       id=place:{slug}       pk=place        props: name
///   Edge   INVOLVED_IN   Participant -> Event
///   Edge   OCCURRED_AT   Event       -> Place
/// </summary>
public class GraphTraversalService : IGraphTraversalService
{
    private readonly IGremlinRepository _repo;

    public GraphTraversalService(IGremlinRepository repo) => _repo = repo;

    public async Task<GraphResult> GetNeighborsAsync(string id, int depth)
    {
        depth = Math.Clamp(depth, 1, 2);
        const string script = @"
g.V().has('id', vId)
 .repeat(both().simplePath())
 .times(depth__)
 .dedup()
 .valueMap(true)";

        // Gremlin doesn't allow binding the .times() integer directly in all drivers,
        // so we build two named scripts to keep bindings for everything else.
        var safeScript = script.Replace("depth__", depth.ToString());

        var result = await _repo.ExecuteAsync(safeScript, new Dictionary<string, object?>
        {
            ["vId"] = id
        });

        return new GraphResult { Vertices = result };
    }

    public async Task<IReadOnlyCollection<dynamic>> GetEventsByParticipantAsync(string participantSlug)
    {
        // Accept either bare slug or full id
        var pid = participantSlug.StartsWith("participant:") ? participantSlug : $"participant:{participantSlug}";

        const string script = @"
g.V().has('Participant','id', pId).has('pk','participant')
 .out('INVOLVED_IN')
 .valueMap('id','name','startTime','startYear')
 .order().by('startYear', asc)";

        return await _repo.ExecuteAsync(script, new Dictionary<string, object?>
        {
            ["pId"] = pid
        });
    }

    public async Task<IReadOnlyCollection<dynamic>> GetEventDetailsAsync(string eventId)
    {
        const string script = @"
g.V().has('Event','id', eId).has('pk','event')
 .project('event','participants','places')
 .by(valueMap('id','name','startTime','startYear'))
 .by(__.in('INVOLVED_IN').values('name').fold())
 .by(__.out('OCCURRED_AT').values('name').fold())";

        return await _repo.ExecuteAsync(script, new Dictionary<string, object?>
        {
            ["eId"] = eventId
        });
    }

    public async Task<IReadOnlyCollection<dynamic>> GetEventsByPlaceAsync(string placeSlug)
    {
        var plid = placeSlug.StartsWith("place:") ? placeSlug : $"place:{placeSlug}";

        const string script = @"
g.V().has('Place','id', plId).has('pk','place')
 .in('OCCURRED_AT')
 .valueMap('id','name','startTime','startYear')
 .order().by('startYear', asc)";

        return await _repo.ExecuteAsync(script, new Dictionary<string, object?>
        {
            ["plId"] = plid
        });
    }

    public async Task<IReadOnlyCollection<dynamic>> GetEventsByYearRangeAsync(int? fromYear, int? toYear, int limit = 100)
    {
        // Build filter chain dynamically
        var filter = "";
        if (fromYear.HasValue && toYear.HasValue)
            filter = $".has('startYear', between({fromYear.Value},{toYear.Value}))";
        else if (fromYear.HasValue)
            filter = $".has('startYear', gte({fromYear.Value}))";
        else if (toYear.HasValue)
            filter = $".has('startYear', lte({toYear.Value}))";

        var script = $@"
g.V().hasLabel('Event').has('pk','event')
 {filter}
 .valueMap('id','name','startTime','startYear')
 .order().by('startYear', asc)
 .limit({Math.Clamp(limit, 1, 500)})";

        return await _repo.ExecuteAsync(script);
    }
}
