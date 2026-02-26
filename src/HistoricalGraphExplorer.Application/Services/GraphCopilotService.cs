using HistoricalGraphExplorer.Application.Interfaces;
using Microsoft.SemanticKernel;
using System.Text.Json;

namespace HistoricalGraphExplorer.Application.Services;

public class GraphCopilotService : IGraphCopilotService
{
    private readonly Kernel _kernel;
    private readonly IGremlinRepository _repo;

    // Exact schema that the C# uploader produces — injected into every AI prompt
    private const string GraphSchema = """
    GRAPH SCHEMA (Azure Cosmos DB Gremlin API):
    ─────────────────────────────────────────────
    VERTEX: Event
      id        = "wd:Q<number>"   e.g. "wd:Q74302"
      pk        = "event"
      name      = string           e.g. "Great Turkish War"
      startTime = ISO string       e.g. "1683-01-01T00:00:00Z"
      startYear = integer          e.g. 1683  (negative for BCE)

    VERTEX: Participant
      id   = "participant:<slug>"  e.g. "participant:ottoman-empire"
      pk   = "participant"
      name = string                e.g. "Ottoman Empire"

    VERTEX: Place
      id   = "place:<slug>"        e.g. "place:ukraine"
      pk   = "place"
      name = string                e.g. "Ukraine"

    EDGES:
      Participant -[INVOLVED_IN]-> Event
      Event       -[OCCURRED_AT]-> Place

    ─────────────────────────────────────────────
    EXAMPLE QUERIES:
    
    Q: Wars involving the Ottoman Empire?
    A: g.V().has('Participant','id','participant:ottoman-empire').out('INVOLVED_IN').valueMap('name','startYear')

    Q: All participants of the Great Turkish War?
    A: g.V().has('Event','id','wd:Q74623').in('INVOLVED_IN').values('name')

    Q: Events that took place in Ukraine?
    A: g.V().has('Place','id','place:ukraine').in('OCCURRED_AT').valueMap('name','startYear')

    Q: Events after 1800?
    A: g.V().hasLabel('Event').has('startYear',gt(1800)).valueMap('name','startYear').order().by('startYear',asc).limit(50)

    Q: Events between 1600 and 1700?
    A: g.V().hasLabel('Event').has('startYear',between(1600,1700)).valueMap('name','startYear').order().by('startYear',asc)

    Q: Find participant slug for "Holy Roman Empire":
       slug = to-lowercase, replace non-alphanumeric with '-', collapse multiple dashes
       => "holy-roman-empire"  => id = "participant:holy-roman-empire"
    ─────────────────────────────────────────────
    """;

    public GraphCopilotService(Kernel kernel, IGremlinRepository repo)
    {
        _kernel = kernel;
        _repo = repo;
    }

    public async Task<string> AskAsync(string question)
    {
        // Step 1: AI generates Gremlin from schema + question
        var gremlinPrompt = $"""
        {GraphSchema}

        Convert the following natural language question into a SAFE Gremlin query using the exact schema above.

        RULES:
        - Only use: g.V(), hasLabel(), has(), both(), out(), in(), outE(), inV(), 
                    values(), valueMap(), dedup(), limit(), order(), by(), project(),
                    asc, desc, gt(), lt(), gte(), lte(), between(), where(), fold(), unfold()
        - Max traversal depth: 2
        - NO write operations: no addV, addE, drop, property()
        - Always include .limit(50) unless already bounded
        - Return ONLY the Gremlin query — no explanation, no markdown, no code fences

        QUESTION: {question}
        """;

        var rawQuery = (await _kernel.InvokePromptAsync(gremlinPrompt)).ToString().Trim();
        // Strip accidental code fences the model might add
        var query = rawQuery.Replace("```groovy", "").Replace("```gremlin", "").Replace("```", "").Trim();

        if (!IsSafe(query))
            return "⚠️ Unsafe query rejected — it contained a write operation.";

        // Step 2: Execute against graph
        IReadOnlyCollection<dynamic> data;
        try
        {
            data = await _repo.ExecuteAsync(query);
        }
        catch (Exception ex)
        {
            return $"⚠️ Graph query failed: {ex.Message}\n\nGenerated query was:\n{query}";
        }

        if (!data.Any())
            return "No matching records found in the graph for that question.";

        // Step 3: AI summarises results
        var summaryPrompt = $"""
        The user asked: {question}

        The graph database returned:
        {JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false })}

        Write a clear, concise answer in plain English.
        List specific names/dates where helpful. Do not mention Gremlin or the database internals.
        """;

        return (await _kernel.InvokePromptAsync(summaryPrompt)).ToString();
    }

    private static bool IsSafe(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        var blocked = new[] { ".drop()", "addV(", "addE(", ".property(", "addVertex", "addEdge" };
        return !blocked.Any(x => query.Contains(x, StringComparison.OrdinalIgnoreCase));
    }
}
