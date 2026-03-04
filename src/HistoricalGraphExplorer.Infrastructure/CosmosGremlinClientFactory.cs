using Gremlin.Net.Driver;
using Gremlin.Net.Structure.IO.GraphSON;

namespace HistoricalGraphExplorer.Infrastructure;

public static class CosmosGremlinClientFactory
{
    /// <summary>
    /// Creates a GremlinClient using GraphSON2MessageSerializer — the same
    /// serializer used by the C# uploader (GraphSONSerializersV2d0).
    /// Config section: CosmosGremlin (Endpoint, Database, Graph, PrimaryKey).
    /// </summary>
    public static GremlinClient Create(string endpoint, string primaryKey, string database, string graph)
    {
        var uri = new Uri(endpoint);

        var server = new GremlinServer(
            hostname: uri.Host,
            port: uri.Port > 0 ? uri.Port : 443,
            enableSsl: uri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase),
            username: $"/dbs/{database}/colls/{graph}",
            password: primaryKey);

        return new GremlinClient(server, new GraphSON2MessageSerializer());
    }
}
