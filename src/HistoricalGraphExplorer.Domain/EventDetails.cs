namespace HistoricalGraphExplorer.Domain;

public record EventDetails(
    EventVertex Event,
    IReadOnlyList<string> Participants,
    IReadOnlyList<string> Places
);
