using HistoricalGraphExplorer.Application.Interfaces;
using HistoricalGraphExplorer.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using System.Text;
using System.Text.Json;

namespace HistoricalGraphExplorer.Application.Services;

/// <summary>
/// Copilot service with lightweight conversational memory.
///
/// Pipeline per request:
///   1. Load ConversationState (structured memory, 0 tokens)
///   2. ContextResolver — rewrite pronoun references (0 AI calls)
///   3. ContextResolver — try in-memory filter (0 DB call)
///   4. HybridRouter — deterministic regex routing (0 AI calls, ~50% of requests)
///   5. AI intent classification — only when regex fails (small call, temp=0.0)
///   6. Typed graph traversal
///   7. AI summary (small call, temp=0.1)
///   8. Update ConversationState
/// </summary>
public class GraphCopilotService : IGraphCopilotService
{
    private readonly Kernel _kernel;
    private readonly IGraphTraversalService _traversal;
    private readonly IConversationStore _store;
    private readonly ILogger<GraphCopilotService> _logger;

    private const string SystemMessage = """
    You are a historical graph assistant. The graph contains ~780 historical events.
    Schema:
      Event: id=wd:Q<n>, name, startYear (int, negative=BCE), startTime (ISO)
      Participant: id=participant:<slug>, name  (slug = lowercase, hyphens)
      Place: id=place:<slug>, name
      Edges: Participant-[INVOLVED_IN]->Event, Event-[OCCURRED_AT]->Place

    Answer strictly using the provided records. If records are empty, say no data was found.
    Do not mention database internals, Gremlin, or graph concepts in your answer.
    """;

    public GraphCopilotService(
        Kernel kernel,
        IGraphTraversalService traversal,
        IConversationStore store,
        ILogger<GraphCopilotService> logger)
    {
        _kernel    = kernel;
        _traversal = traversal;
        _store     = store;
        _logger    = logger;
    }

    public async Task<string> AskAsync(string question, string? sessionId = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Step 1: Load conversation state
        var hasSession = !string.IsNullOrWhiteSpace(sessionId);
        var state = hasSession ? _store.GetOrCreate(sessionId!) : null;

        // Step 2: Context resolver — rewrite pronouns (0 AI calls)
        var resolvedQuestion = ContextResolver.Resolve(question, state, _logger);

        // Step 3: Try in-memory filter (0 DB call, 0 AI calls)
        IReadOnlyCollection<EventVertex>? inMemoryResults = null;
        if (state is not null)
            inMemoryResults = ContextResolver.TryFilterInMemory(resolvedQuestion, state, _logger);

        string data;

        if (inMemoryResults is not null)
        {
            // Update LastEvents so the user sees the filtered subset,
            // but BaseEvents is intentionally left unchanged (see ContextResolver).
            if (state is not null)
                state.LastEvents = inMemoryResults.ToList();
            data = FormatEvents(inMemoryResults);
        }
        else
        {
            // Step 4: HybridRouter — deterministic regex (0 AI calls)
            var intent = HybridRouter.TryResolve(resolvedQuestion, _logger);

            // Step 5: AI intent classification — only if regex failed
            if (intent is null)
                intent = await ClassifyIntentAsync(resolvedQuestion);

            _logger.LogInformation(
                "[Copilot] Intent={Intent} P={P} Pl={Pl} F={F} T={T} EId={E}",
                intent.Intent, intent.Participant, intent.Place,
                intent.FromYear, intent.ToYear, intent.EventId);

            // Step 6: Typed graph traversal
            data = await FetchDataAsync(intent, state);

            // Update resolved entities in state
            if (hasSession && state is not null)
                UpdateState(state, intent);
        }

        if (string.IsNullOrWhiteSpace(data))
        {
            _logger.LogInformation("[Copilot] No records found. question={Q}", question);
            return "No matching records found in the graph for that question.";
        }

        // Step 7: AI summary (temp=0.1, stateless — no chat history sent)
        var answer = await SummariseAsync(question, data);

        sw.Stop();
        _logger.LogInformation("[Copilot] Done. question={Q} ms={Ms}", question, sw.ElapsedMilliseconds);

        // Step 8: Persist updated state
        if (hasSession && state is not null)
            _store.Save(sessionId!, state);

        return answer;
    }

    // ── Intent Classification (AI, temp=0.0) ─────────────────────────────────

    private async Task<QueryIntent> ClassifyIntentAsync(string question)
    {
        var prompt = $"""
        Classify this historical question into exactly one JSON intent object.

        Intent types:
        - ParticipantQuery: asking about events involving a specific country/participant
        - PlaceQuery: asking about events at a specific location
        - YearRangeQuery: asking about events in a time period
        - EventDetailsQuery: asking about one specific named event

        Rules:
        - participant: lowercase hyphenated slug (e.g. "ottoman-empire")
        - place: lowercase hyphenated slug (e.g. "ukraine")
        - eventId: Wikidata id if known (e.g. "wd:Q74623"), else null
        - fromYear/toYear: integers, negative for BCE, null if not applicable
        - Return ONLY valid JSON, no markdown.

        Examples:
        Q: "Which wars involved the Ottoman Empire?"
        {{"intent":"ParticipantQuery","participant":"ottoman-empire","place":null,"eventId":null,"fromYear":null,"toYear":null}}

        Q: "What events took place in Ukraine?"
        {{"intent":"PlaceQuery","participant":null,"place":"ukraine","eventId":null,"fromYear":null,"toYear":null}}

        Question: {question}
        """;

        string raw;
        try
        {
            raw = (await _kernel.InvokePromptAsync(prompt,
                new KernelArguments(new AzureOpenAIPromptExecutionSettings { Temperature = 0.0 }))).ToString().Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Copilot] Intent classification failed, using Unknown");
            return new QueryIntent(QueryIntentType.Unknown);
        }

        raw = raw.Replace("```json", "").Replace("```", "").Trim();

        try
        {
            var doc  = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var intentStr = root.TryGetProperty("intent", out var iv) ? iv.GetString() : null;
            var intent = intentStr switch
            {
                "ParticipantQuery"  => QueryIntentType.ParticipantQuery,
                "PlaceQuery"        => QueryIntentType.PlaceQuery,
                "YearRangeQuery"    => QueryIntentType.YearRangeQuery,
                "EventDetailsQuery" => QueryIntentType.EventDetailsQuery,
                _                   => QueryIntentType.Unknown
            };

            return new QueryIntent(
                Intent:      intent,
                Participant: GetStringOrNull(root, "participant"),
                Place:       GetStringOrNull(root, "place"),
                EventId:     GetStringOrNull(root, "eventId"),
                FromYear:    GetIntOrNull(root, "fromYear"),
                ToYear:      GetIntOrNull(root, "toYear")
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Copilot] Failed to parse intent JSON: {Raw}", raw);
            return new QueryIntent(QueryIntentType.Unknown);
        }
    }

    // ── Data Fetching ─────────────────────────────────────────────────────────

    private async Task<string> FetchDataAsync(QueryIntent intent, ConversationState? state)
    {
        IReadOnlyCollection<EventVertex>? events = null;

        switch (intent.Intent)
        {
            case QueryIntentType.ParticipantQuery when !string.IsNullOrWhiteSpace(intent.Participant):
                events = await _traversal.GetEventsByParticipantAsync(intent.Participant!);
                break;

            case QueryIntentType.PlaceQuery when !string.IsNullOrWhiteSpace(intent.Place):
                events = await _traversal.GetEventsByPlaceAsync(intent.Place!);
                break;

            case QueryIntentType.YearRangeQuery:
                events = await _traversal.GetEventsByYearRangeAsync(intent.FromYear, intent.ToYear);
                break;

            case QueryIntentType.EventDetailsQuery when !string.IsNullOrWhiteSpace(intent.EventId):
                var details = await _traversal.GetEventDetailsAsync(intent.EventId!);
                return FormatEventDetails(details);

            default:
                events = await _traversal.GetEventsByYearRangeAsync(null, null, 50);
                break;
        }

        // Store up to 50 events in session memory.
        // BaseEvents = canonical full set (never overwritten by follow-up year filters).
        // LastEvents  = the set just returned to the user (may be a filtered subset later).
        if (state is not null && events is not null)
        {
            var capped = events.Take(50).ToList();
            state.BaseEvents = capped;
            state.LastEvents = capped;
            _logger.LogInformation("[Copilot] Cached {Count} events in session memory (BaseEvents + LastEvents)", capped.Count);
        }

        return events is not null ? FormatEvents(events) : "";
    }

    // ── State Update ─────────────────────────────────────────────────────────

    private static void UpdateState(ConversationState state, QueryIntent intent)
    {
        // When the primary entity changes (new participant or place),
        // reset BaseEvents so stale follow-ups don't leak across topics.
        bool entityChanged =
            (!string.IsNullOrWhiteSpace(intent.Participant) && intent.Participant != state.LastParticipant) ||
            (!string.IsNullOrWhiteSpace(intent.Place)       && intent.Place       != state.LastPlace);

        if (entityChanged)
        {
            state.BaseEvents.Clear();
            state.LastEvents.Clear();
        }

        if (!string.IsNullOrWhiteSpace(intent.Participant)) state.LastParticipant = intent.Participant;
        if (!string.IsNullOrWhiteSpace(intent.Place))       state.LastPlace       = intent.Place;
        if (intent.FromYear.HasValue) state.LastFromYear = intent.FromYear;
        if (intent.ToYear.HasValue)   state.LastToYear   = intent.ToYear;
        state.LastUpdated = DateTime.UtcNow;
    }

    // ── AI Summary (temp=0.1, stateless — no history) ────────────────────────

    private async Task<string> SummariseAsync(string question, string data)
    {
        var prompt = $"""
        User question: {question}

        Graph records:
        {data}

        Answer the question using only the records above.
        List specific event names and years where relevant.
        If the records do not contain enough information, say so clearly.
        """;

        try
        {
            return (await _kernel.InvokePromptAsync(prompt,
                new KernelArguments(new AzureOpenAIPromptExecutionSettings { Temperature = 0.1 }))).ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Copilot] Summary call failed");
            return "⚠️ Could not generate a summary. Please try again.";
        }
    }

    // ── Formatters ───────────────────────────────────────────────────────────

    private static string FormatEvents(IReadOnlyCollection<EventVertex> events)
    {
        if (!events.Any()) return "";
        var sb = new StringBuilder();
        foreach (var e in events)
        {
            var year = e.StartYear.HasValue ? e.StartYear.Value.ToString() : "unknown year";
            sb.AppendLine($"- {e.Name} ({year})");
        }
        return sb.ToString();
    }

    private static string FormatEventDetails(EventDetails? details)
    {
        if (details is null) return "";
        var sb = new StringBuilder();
        sb.AppendLine($"Event: {details.Event.Name} ({details.Event.StartYear})");
        if (details.Participants.Any())
            sb.AppendLine($"Participants: {string.Join(", ", details.Participants)}");
        if (details.Places.Any())
            sb.AppendLine($"Places: {string.Join(", ", details.Places)}");
        return sb.ToString();
    }

    // ── JSON helpers ─────────────────────────────────────────────────────────

    private static string? GetStringOrNull(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetIntOrNull(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        return null;
    }
}
