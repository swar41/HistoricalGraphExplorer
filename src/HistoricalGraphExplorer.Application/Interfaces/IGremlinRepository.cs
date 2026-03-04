namespace HistoricalGraphExplorer.Application.Interfaces;

public interface IGremlinRepository
{
    Task<IReadOnlyCollection<dynamic>> ExecuteAsync(string script, IDictionary<string, object?>? bindings = null);
}
