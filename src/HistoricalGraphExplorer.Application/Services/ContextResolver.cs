using HistoricalGraphExplorer.Domain;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace HistoricalGraphExplorer.Application.Services;

/// <summary>
/// Resolves contextual/pronoun references in follow-up questions
/// by injecting structured memory — no AI call, zero tokens.
///
/// In-memory filtering is restricted to year-only predicates.
/// Place-based follow-ups (e.g. "which of them were in Europe") are NOT
/// filtered in-memory because event names do not reliably encode place data.
/// Those questions fall through to a fresh DB query.
/// </summary>
public static class ContextResolver
{
    // Pronouns / references that signal a follow-up question
    private static readonly string[] ContextualTriggers =
    [
        "them", "those", "they", "it", "that war", "that event",
        "that battle", "those events", "those wars", "these", "this"
    ];

    private static readonly Regex YearAfterPattern  = new(@"\bafter\s+(\d{1,4})\b",  RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex YearBeforePattern = new(@"\bbefore\s+(\d{1,4})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BetweenYearsPattern = new(
        @"\bbetween\s+(-?\d{1,4})\s+and\s+(-?\d{1,4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Detect place-intent keywords — used to explicitly route to DB instead of in-memory
    private static readonly Regex PlaceFollowUpPattern = new(
        @"\b(?:in|at|near|around)\s+[a-z]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Rewrites the question by injecting context from previous state.
    /// Returns the (possibly rewritten) question.
    /// </summary>
    public static string Resolve(string question, ConversationState? state, ILogger? logger = null)
    {
        if (state is null || !state.HasContext) return question;
        if (!IsContextual(question)) return question;

        var rewritten = question;

        if (state.LastParticipant is not null &&
            !question.Contains(state.LastParticipant, StringComparison.OrdinalIgnoreCase))
        {
            rewritten = rewritten
                .Replace("those events", $"events involving {state.LastParticipant}", StringComparison.OrdinalIgnoreCase)
                .Replace("those wars",   $"wars involving {state.LastParticipant}",   StringComparison.OrdinalIgnoreCase)
                .Replace("those",        $"events involving {state.LastParticipant}",  StringComparison.OrdinalIgnoreCase)
                .Replace("them",         $"events involving {state.LastParticipant}",  StringComparison.OrdinalIgnoreCase)
                .Replace("they",         $"events involving {state.LastParticipant}",  StringComparison.OrdinalIgnoreCase)
                .Replace("it",           state.LastParticipant,                         StringComparison.OrdinalIgnoreCase);
        }
        else if (state.LastPlace is not null &&
                 !question.Contains(state.LastPlace, StringComparison.OrdinalIgnoreCase))
        {
            rewritten = rewritten
                .Replace("those events", $"events in {state.LastPlace}", StringComparison.OrdinalIgnoreCase)
                .Replace("them",         $"events in {state.LastPlace}", StringComparison.OrdinalIgnoreCase)
                .Replace("those",        $"events in {state.LastPlace}", StringComparison.OrdinalIgnoreCase)
                .Replace("they",         $"events in {state.LastPlace}", StringComparison.OrdinalIgnoreCase)
                .Replace("it",           state.LastPlace,                 StringComparison.OrdinalIgnoreCase);
        }

        if (rewritten != question)
            logger?.LogInformation("[ContextResolver] Rewritten: '{Original}' → '{Rewritten}'", question, rewritten);

        return rewritten;
    }

    /// <summary>
    /// Tries to satisfy a follow-up using the cached BaseEvents — YEAR FILTERS ONLY.
    ///
    /// Place-based follow-ups are explicitly excluded: event names do not encode place
    /// information, so "which of them were in Europe" cannot be answered correctly
    /// from event names alone. Those questions return null and trigger a DB query.
    ///
    /// Always filters from BaseEvents (the original full set), NOT from the previous
    /// turn's filtered result — this prevents cascading over-narrowing across turns:
    ///   Q1: Ottoman wars      → BaseEvents = [15 events]
    ///   Q2: which after 1700  → filter BaseEvents → 8 events  (BaseEvents unchanged)
    ///   Q3: which before 1800 → filter BaseEvents → 10 events (not 8→ further narrowed)
    /// </summary>
    public static IReadOnlyCollection<EventVertex>? TryFilterInMemory(
        string question,
        ConversationState state,
        ILogger? logger = null)
    {
        if (!state.BaseEvents.Any()) return null;
        if (!IsContextual(question))  return null;

        // Explicitly reject place-based follow-ups — send to DB
        if (PlaceFollowUpPattern.IsMatch(question))
        {
            logger?.LogInformation("[ContextResolver] Place-based follow-up detected — routing to DB (not in-memory)");
            return null;
        }

        var events = state.BaseEvents.AsEnumerable();
        bool filtered = false;

        // between X and Y — check first (more specific than after/before)
        var betweenMatch = BetweenYearsPattern.Match(question);
        if (betweenMatch.Success &&
            int.TryParse(betweenMatch.Groups[1].Value, out var betweenFrom) &&
            int.TryParse(betweenMatch.Groups[2].Value, out var betweenTo))
        {
            events = events.Where(e => e.StartYear.HasValue &&
                                       e.StartYear.Value >= betweenFrom &&
                                       e.StartYear.Value <= betweenTo);
            filtered = true;
        }
        else
        {
            // after YYYY
            var afterMatch = YearAfterPattern.Match(question);
            if (afterMatch.Success && int.TryParse(afterMatch.Groups[1].Value, out var afterYear))
            {
                events = events.Where(e => e.StartYear.HasValue && e.StartYear.Value > afterYear);
                filtered = true;
            }

            // before YYYY
            var beforeMatch = YearBeforePattern.Match(question);
            if (beforeMatch.Success && int.TryParse(beforeMatch.Groups[1].Value, out var beforeYear))
            {
                events = events.Where(e => e.StartYear.HasValue && e.StartYear.Value < beforeYear);
                filtered = true;
            }
        }

        if (!filtered) return null;

        var result = events.ToList();
        logger?.LogInformation(
            "[ContextResolver] In-memory year filter — {Count}/{Total} events returned (no DB call)",
            result.Count, state.BaseEvents.Count);

        return result;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    public static bool IsContextual(string question) => ContainsTrigger(question);

    private static bool ContainsTrigger(string q) =>
        ContextualTriggers.Any(t => q.Contains(t, StringComparison.OrdinalIgnoreCase));
}
