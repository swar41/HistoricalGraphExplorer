using Gremlin.Net.Driver;
using Gremlin.Net.Driver.Messages;
using HistoricalGraphExplorer.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace HistoricalGraphExplorer.Infrastructure;

/// <summary>
/// Executes parameterised Gremlin scripts against Azure Cosmos DB.
/// Uses Gremlin.Net RequestMessage with bindings — same as the C# uploader.
/// Includes improved whitelist-based safety validation.
/// </summary>
public class GremlinRepository : IGremlinRepository
{
    private readonly GremlinClient _client;
    private readonly ILogger<GremlinRepository> _logger;

    // Blocked operations — write and dangerous patterns
    private static readonly string[] BlockedTerms =
    [
        ".drop()", "addV(", "addE(", ".property(", "addVertex", "addEdge",
        ".iterate()", ".toList().remove"
    ];

    // Warn when traversal depth is excessive
    private static readonly Regex ExcessiveDepthPattern = new(@"\.both\(\)\.both\(\)\.both\(\)", RegexOptions.Compiled);

    // Warn when no limit is present
    private static readonly Regex LimitPattern = new(@"\.limit\(\s*\d+\s*\)", RegexOptions.Compiled);

    public GremlinRepository(GremlinClient client, ILogger<GremlinRepository> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<dynamic>> ExecuteAsync(
        string script,
        IDictionary<string, object?>? bindings = null)
    {
        ValidateScript(script);

        _logger.LogDebug("[GremlinRepo] Executing script: {Script}", script);

        var request = RequestMessage.Build(Tokens.OpsEval)
            .AddArgument(Tokens.ArgsGremlin, script)
            .AddArgument(Tokens.ArgsBindings, bindings ?? new Dictionary<string, object?>())
            .Create();

        return await _client.SubmitAsync<dynamic>(request);
    }

    private void ValidateScript(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            throw new ArgumentException("Gremlin script must not be empty.");

        foreach (var term in BlockedTerms)
        {
            if (script.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[GremlinRepo] Blocked script containing '{Term}': {Script}", term, script);
                throw new InvalidOperationException($"Unsafe Gremlin operation blocked: {term}");
            }
        }

        if (ExcessiveDepthPattern.IsMatch(script))
        {
            _logger.LogWarning("[GremlinRepo] Excessive traversal depth detected: {Script}", script);
            throw new InvalidOperationException("Traversal depth exceeds allowed limit (max 2 hops).");
        }

        if (!script.TrimStart().StartsWith("g.V()", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("[GremlinRepo] Script does not start with g.V(): {Script}", script);
            throw new InvalidOperationException("Only g.V() read traversals are permitted.");
        }

        if (!LimitPattern.IsMatch(script))
        {
            _logger.LogWarning("[GremlinRepo] Script missing .limit(): {Script}", script);
            // Warn only — traversal service already applies limits
        }
    }
}
