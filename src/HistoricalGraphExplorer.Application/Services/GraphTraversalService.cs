using HistoricalGraphExplorer.Application.Interfaces;
using HistoricalGraphExplorer.Domain;
using Microsoft.Extensions.Logging;

namespace HistoricalGraphExplorer.Application.Services;

/// <summary>
/// All queries use parameterised bindings — matches the C# uploader's approach.
/// All results are mapped to typed DTOs before returning (no raw dynamic).
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
    private readonly ILogger<GraphTraversalService> _logger;

    public GraphTraversalService(IGremlinRepository repo, ILogger<GraphTraversalService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<GraphResult> GetNeighborsAsync(string id, int depth)
    {
        depth = Math.Clamp(depth, 1, 2);
        var safeScript = $@"
g.V().has('id', vId)
 .repeat(both().simplePath())
 .times({depth})
 .dedup()
 .valueMap(true)";

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await _repo.ExecuteAsync(safeScript, new Dictionary<string, object?> { ["vId"] = id });
        sw.Stop();
        _logger.LogInformation("[GraphTraversal] GetNeighborsAsync id={Id} depth={Depth} results={Count} ms={Ms}",
            id, depth, result.Count, sw.ElapsedMilliseconds);

        return new GraphResult { Vertices = result };
    }

    public async Task<IReadOnlyCollection<EventVertex>> GetEventsByParticipantAsync(string participantSlug)
    {
        var pid = participantSlug.StartsWith("participant:") ? participantSlug : $"participant:{participantSlug}";

        const string script = @"
g.V().has('Participant','id', pId).has('pk','participant')
 .out('INVOLVED_IN')
 .valueMap('id','name','startTime','startYear')
 .order().by('startYear', asc)";

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var raw = await _repo.ExecuteAsync(script, new Dictionary<string, object?> { ["pId"] = pid });
        sw.Stop();
        _logger.LogInformation("[GraphTraversal] GetEventsByParticipantAsync slug={Slug} results={Count} ms={Ms}",
            participantSlug, raw.Count, sw.ElapsedMilliseconds);

        return raw.Select(MapEventVertex).ToList();
    }

    public async Task<EventDetails?> GetEventDetailsAsync(string eventId)
    {
        const string script = @"
g.V().has('Event','id', eId).has('pk','event')
 .project('event','participants','places')
 .by(valueMap('id','name','startTime','startYear'))
 .by(__.in('INVOLVED_IN').values('name').fold())
 .by(__.out('OCCURRED_AT').values('name').fold())";

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var raw = await _repo.ExecuteAsync(script, new Dictionary<string, object?> { ["eId"] = eventId });
        sw.Stop();
        _logger.LogInformation("[GraphTraversal] GetEventDetailsAsync id={Id} ms={Ms}", eventId, sw.ElapsedMilliseconds);

        if (!raw.Any()) return null;

        var row = raw.First() as IDictionary<string, object>;
        if (row is null) return null;

        var eventMap = row["event"] as IDictionary<string, object>;
        var ev = eventMap is null ? new EventVertex(eventId, "", null, null) : MapEventVertex(eventMap);

        var participants = ExtractStringList(row, "participants");
        var places = ExtractStringList(row, "places");

        return new EventDetails(ev, participants, places);
    }

    public async Task<IReadOnlyCollection<EventVertex>> GetEventsByPlaceAsync(string placeSlug)
    {
        var plid = placeSlug.StartsWith("place:") ? placeSlug : $"place:{placeSlug}";

        const string script = @"
g.V().has('Place','id', plId).has('pk','place')
 .in('OCCURRED_AT')
 .valueMap('id','name','startTime','startYear')
 .order().by('startYear', asc)";

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var raw = await _repo.ExecuteAsync(script, new Dictionary<string, object?> { ["plId"] = plid });
        sw.Stop();
        _logger.LogInformation("[GraphTraversal] GetEventsByPlaceAsync slug={Slug} results={Count} ms={Ms}",
            placeSlug, raw.Count, sw.ElapsedMilliseconds);

        return raw.Select(MapEventVertex).ToList();
    }

    public async Task<IReadOnlyCollection<EventVertex>> GetEventsByYearRangeAsync(int? fromYear, int? toYear, int limit = 100)
    {
        // Build parameterised filter — avoids string interpolation of user-controlled values
        string script;
        var bindings = new Dictionary<string, object?>();
        var clampedLimit = Math.Clamp(limit, 1, 500);

        if (fromYear.HasValue && toYear.HasValue)
        {
            bindings["fromYear"] = fromYear.Value;
            bindings["toYear"] = toYear.Value;
            script = $@"
g.V().hasLabel('Event').has('pk','event')
 .has('startYear', between(fromYear, toYear))
 .valueMap('id','name','startTime','startYear')
 .order().by('startYear', asc)
 .limit({clampedLimit})";
        }
        else if (fromYear.HasValue)
        {
            bindings["fromYear"] = fromYear.Value;
            script = $@"
g.V().hasLabel('Event').has('pk','event')
 .has('startYear', gte(fromYear))
 .valueMap('id','name','startTime','startYear')
 .order().by('startYear', asc)
 .limit({clampedLimit})";
        }
        else if (toYear.HasValue)
        {
            bindings["toYear"] = toYear.Value;
            script = $@"
g.V().hasLabel('Event').has('pk','event')
 .has('startYear', lte(toYear))
 .valueMap('id','name','startTime','startYear')
 .order().by('startYear', asc)
 .limit({clampedLimit})";
        }
        else
        {
            script = $@"
g.V().hasLabel('Event').has('pk','event')
 .valueMap('id','name','startTime','startYear')
 .order().by('startYear', asc)
 .limit({clampedLimit})";
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var raw = await _repo.ExecuteAsync(script, bindings);
        sw.Stop();
        _logger.LogInformation("[GraphTraversal] GetEventsByYearRangeAsync from={From} to={To} limit={Limit} results={Count} ms={Ms}",
            fromYear, toYear, clampedLimit, raw.Count, sw.ElapsedMilliseconds);

        return raw.Select(MapEventVertex).ToList();
    }

    // ── Mapping helpers ──────────────────────────────────────────────────────

    /// <summary>Maps a GraphSON valueMap row to a clean EventVertex DTO.</summary>
    private static EventVertex MapEventVertex(dynamic row)
    {
        var map = row as IDictionary<string, object> ?? new Dictionary<string, object>();
        return new EventVertex(
            Id:        ExtractFirst(map, "id")        ?? "",
            Name:      ExtractFirst(map, "name")      ?? "",
            StartTime: ExtractFirst(map, "startTime"),
            StartYear: ExtractFirstInt(map, "startYear")
        );
    }

    private static string? ExtractFirst(IDictionary<string, object> map, string key)
    {
        if (!map.TryGetValue(key, out var val)) return null;
        if (val is System.Collections.IEnumerable list and not string)
        {
            foreach (var item in list) return item?.ToString();
        }
        return val?.ToString();
    }

    private static int? ExtractFirstInt(IDictionary<string, object> map, string key)
    {
        var s = ExtractFirst(map, key);
        return int.TryParse(s, out var i) ? i : null;
    }

    private static IReadOnlyList<string> ExtractStringList(IDictionary<string, object> row, string key)
    {
        if (!row.TryGetValue(key, out var val)) return [];
        if (val is System.Collections.IEnumerable list and not string)
            return list.Cast<object>().Select(o => o?.ToString() ?? "").Where(s => s.Length > 0).ToList();
        return [];
    }
}
