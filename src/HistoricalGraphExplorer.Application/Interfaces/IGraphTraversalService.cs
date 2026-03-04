using HistoricalGraphExplorer.Domain;

namespace HistoricalGraphExplorer.Application.Interfaces;

public interface IGraphTraversalService
{
    /// <summary>Traverse neighbors of any vertex (Event, Participant, or Place).</summary>
    Task<GraphResult> GetNeighborsAsync(string id, int depth);

    /// <summary>Get all events a participant was involved in.</summary>
    Task<IReadOnlyCollection<EventVertex>> GetEventsByParticipantAsync(string participantSlug);

    /// <summary>Get full details (event + participants + places) for one event.</summary>
    Task<EventDetails?> GetEventDetailsAsync(string eventId);

    /// <summary>Get all events that occurred at a place.</summary>
    Task<IReadOnlyCollection<EventVertex>> GetEventsByPlaceAsync(string placeSlug);

    /// <summary>Get events filtered by year range.</summary>
    Task<IReadOnlyCollection<EventVertex>> GetEventsByYearRangeAsync(int? fromYear, int? toYear, int limit = 100);
}
