namespace HistoricalGraphExplorer.Domain;

/// <summary>
/// Lightweight structured memory per session.
/// Stores only the last resolved entities — NOT raw chat text.
/// Cost: 0 tokens (never sent to OpenAI directly).
/// </summary>
public class ConversationState
{
    public string? LastParticipant { get; set; }
    public string? LastPlace       { get; set; }
    public int?    LastFromYear    { get; set; }
    public int?    LastToYear      { get; set; }

    /// <summary>
    /// The original full set of events fetched from the DB for the current
    /// participant/place/year context. Preserved across follow-up year filters
    /// so multiple follow-ups always filter from the full original result set
    /// rather than cascading filters narrowing too aggressively.
    /// Max 50 entries.
    /// </summary>
    public List<EventVertex> BaseEvents { get; set; } = new();

    /// <summary>
    /// The most recently visible event list (may be a year-filtered subset of BaseEvents).
    /// Returned to the caller but NOT used as the base for the next filter —
    /// BaseEvents is always the canonical source.
    /// </summary>
    public List<EventVertex> LastEvents { get; set; } = new();

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Returns true when the session has enough context to resolve follow-ups.
    /// Auto-resets stale context older than 30 minutes (belt-and-suspenders
    /// alongside store-level eviction).
    /// </summary>
    public bool HasContext
    {
        get
        {
            if (DateTime.UtcNow - LastUpdated > TimeSpan.FromMinutes(30))
            {
                Reset();
                return false;
            }

            return LastParticipant is not null ||
                   LastPlace       is not null ||
                   LastFromYear    is not null ||
                   LastToYear      is not null ||
                   BaseEvents.Count > 0;
        }
    }

    /// <summary>Clears all context fields, keeping the object alive for re-use.</summary>
    public void Reset()
    {
        LastParticipant = null;
        LastPlace       = null;
        LastFromYear    = null;
        LastToYear      = null;
        BaseEvents.Clear();
        LastEvents.Clear();
        LastUpdated = DateTime.UtcNow;
    }
}
