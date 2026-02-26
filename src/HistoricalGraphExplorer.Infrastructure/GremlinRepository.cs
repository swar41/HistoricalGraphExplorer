using Gremlin.Net.Driver;
using Gremlin.Net.Driver.Messages;
using HistoricalGraphExplorer.Application.Interfaces;

namespace HistoricalGraphExplorer.Infrastructure;

/// <summary>
/// Executes parameterised Gremlin scripts against Azure Cosmos DB.
/// Uses Gremlin.Net RequestMessage with bindings — same as the C# uploader.
/// </summary>
public class GremlinRepository : IGremlinRepository
{
    private readonly GremlinClient _client;

    public GremlinRepository(GremlinClient client) => _client = client;

    public async Task<IReadOnlyCollection<dynamic>> ExecuteAsync(
        string script,
        IDictionary<string, object?>? bindings = null)
    {
        var request = RequestMessage.Build(Tokens.OpsEval)
            .AddArgument(Tokens.ArgsGremlin, script)
            .AddArgument(Tokens.ArgsBindings, bindings ?? new Dictionary<string, object?>())
            .Create();

        return await _client.SubmitAsync<dynamic>(request);
    }
}
