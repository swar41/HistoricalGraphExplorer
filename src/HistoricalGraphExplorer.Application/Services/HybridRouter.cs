using HistoricalGraphExplorer.Domain;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace HistoricalGraphExplorer.Application.Services;

/// <summary>
/// Deterministic intent detection via regex — runs BEFORE the AI classification call.
/// Eliminates ~40-60% of intent LLM calls at zero cost.
/// Falls back to AI only when patterns don't match.
/// </summary>
public static class HybridRouter
{
    // Year range: "between 1600 and 1700", "from 1400 to 1500"
    private static readonly Regex BetweenYears = new(
        @"\bbetween\s+(-?\d{1,4})\s+and\s+(-?\d{1,4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FromToYears = new(
        @"\bfrom\s+(-?\d{1,4})\s+to\s+(-?\d{1,4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Single-bound year: "after 1800", "before 500", "in the 1700s"
    private static readonly Regex AfterYear = new(
        @"\bafter\s+(-?\d{1,4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BeforeYear = new(
        @"\bbefore\s+(-?\d{1,4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InTheDecade = new(
        @"\bin\s+the\s+(\d{3,4})s\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Participant: "involving/involved <name>", "wars of <name>"
    private static readonly Regex InvolvingParticipant = new(
        @"\b(?:involving|involved|by|of)\s+(?:the\s+)?([a-z][a-z\s\-]{2,40}?)(?:\?|$|\s+(?:in|after|before|between|during))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Place: "in <place>", "at <place>", "took place in <place>"
    private static readonly Regex InPlace = new(
        @"\b(?:in|at|near|around)\s+(?:the\s+)?([a-z][a-z\s\-]{2,30}?)(?:\?|$|\s+(?:after|before|between|during|involving))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Participant slug helper
    private static string ToSlug(string name) =>
        Regex.Replace(name.ToLowerInvariant().Trim(), @"[^a-z0-9]+", "-").Trim('-');

    // Words that look like places or participants but are actually noise
    private static readonly HashSet<string> Stopwords =
    [
        "wars", "battles", "events", "history", "the", "an", "a",
        "which", "what", "when", "who", "how", "there", "where"
    ];

    /// <summary>
    /// Try to resolve intent deterministically.
    /// Returns null if no confident match — caller should fall back to AI.
    /// </summary>
    public static QueryIntent? TryResolve(string question, ILogger? logger = null)
    {
        // ── Year range ────────────────────────────────────────────────────
        var betweenMatch = BetweenYears.Match(question);
        if (betweenMatch.Success &&
            int.TryParse(betweenMatch.Groups[1].Value, out var from1) &&
            int.TryParse(betweenMatch.Groups[2].Value, out var to1))
        {
            logger?.LogInformation("[HybridRouter] Matched YearRangeQuery (between) {F}-{T}", from1, to1);
            return new QueryIntent(QueryIntentType.YearRangeQuery, FromYear: from1, ToYear: to1);
        }

        var fromToMatch = FromToYears.Match(question);
        if (fromToMatch.Success &&
            int.TryParse(fromToMatch.Groups[1].Value, out var from2) &&
            int.TryParse(fromToMatch.Groups[2].Value, out var to2))
        {
            logger?.LogInformation("[HybridRouter] Matched YearRangeQuery (from-to) {F}-{T}", from2, to2);
            return new QueryIntent(QueryIntentType.YearRangeQuery, FromYear: from2, ToYear: to2);
        }

        var afterMatch = AfterYear.Match(question);
        if (afterMatch.Success && int.TryParse(afterMatch.Groups[1].Value, out var afterYear))
        {
            logger?.LogInformation("[HybridRouter] Matched YearRangeQuery (after) {Y}", afterYear);
            return new QueryIntent(QueryIntentType.YearRangeQuery, FromYear: afterYear);
        }

        var beforeMatch = BeforeYear.Match(question);
        if (beforeMatch.Success && int.TryParse(beforeMatch.Groups[1].Value, out var beforeYear))
        {
            logger?.LogInformation("[HybridRouter] Matched YearRangeQuery (before) {Y}", beforeYear);
            return new QueryIntent(QueryIntentType.YearRangeQuery, ToYear: beforeYear);
        }

        var decadeMatch = InTheDecade.Match(question);
        if (decadeMatch.Success && int.TryParse(decadeMatch.Groups[1].Value, out var decade))
        {
            logger?.LogInformation("[HybridRouter] Matched YearRangeQuery (decade) {D}s", decade);
            return new QueryIntent(QueryIntentType.YearRangeQuery, FromYear: decade, ToYear: decade + 99);
        }

        // ── Participant ───────────────────────────────────────────────────
        var participantMatch = InvolvingParticipant.Match(question);
        if (participantMatch.Success)
        {
            var name = participantMatch.Groups[1].Value.Trim();
            if (!Stopwords.Contains(name.ToLowerInvariant()) && name.Split(' ').Length <= 5)
            {
                var slug = ToSlug(name);
                logger?.LogInformation("[HybridRouter] Matched ParticipantQuery slug={Slug}", slug);
                return new QueryIntent(QueryIntentType.ParticipantQuery, Participant: slug);
            }
        }

        // ── Place ─────────────────────────────────────────────────────────
        var placeMatch = InPlace.Match(question);
        if (placeMatch.Success)
        {
            var name = placeMatch.Groups[1].Value.Trim();
            if (!Stopwords.Contains(name.ToLowerInvariant()) && name.Split(' ').Length <= 4)
            {
                var slug = ToSlug(name);
                logger?.LogInformation("[HybridRouter] Matched PlaceQuery slug={Slug}", slug);
                return new QueryIntent(QueryIntentType.PlaceQuery, Place: slug);
            }
        }

        // No confident match
        logger?.LogInformation("[HybridRouter] No deterministic match — falling back to AI classification");
        return null;
    }
}
