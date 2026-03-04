namespace HistoricalGraphExplorer.Domain;

public record EventVertex(
    string Id,
    string Name,
    string? StartTime,
    int? StartYear
);
