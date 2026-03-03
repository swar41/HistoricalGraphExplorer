namespace HistoricalGraphExplorer.Domain;

public enum QueryIntentType
{
    ParticipantQuery,
    PlaceQuery,
    YearRangeQuery,
    EventDetailsQuery,
    Unknown
}

public record QueryIntent(
    QueryIntentType Intent,
    string? Participant = null,
    string? Place = null,
    string? EventId = null,
    int? FromYear = null,
    int? ToYear = null
);
