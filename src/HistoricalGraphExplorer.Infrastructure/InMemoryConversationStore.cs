using HistoricalGraphExplorer.Application.Interfaces;
using HistoricalGraphExplorer.Domain;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace HistoricalGraphExplorer.Infrastructure;

/// <summary>
/// In-process conversation store using ConcurrentDictionary.
/// Thread-safe for single-instance deployments.
/// Replace with a Redis-backed implementation for multi-instance production.
/// Sessions older than 30 minutes are evicted automatically.
/// </summary>
public class InMemoryConversationStore : IConversationStore
{
    private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, ConversationState> _sessions = new();
    private readonly ILogger<InMemoryConversationStore> _logger;

    public InMemoryConversationStore(ILogger<InMemoryConversationStore> logger)
    {
        _logger = logger;
    }

    public ConversationState GetOrCreate(string sessionId)
    {
        EvictExpired();

        return _sessions.GetOrAdd(sessionId, _ =>
        {
            _logger.LogInformation("[ConversationStore] New session created: {SessionId}", sessionId);
            return new ConversationState();
        });
    }

    public void Save(string sessionId, ConversationState state)
    {
        state.LastUpdated = DateTime.UtcNow;
        _sessions[sessionId] = state;
        _logger.LogDebug("[ConversationStore] Saved session: {SessionId}", sessionId);
    }

    public void Clear(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out _))
            _logger.LogInformation("[ConversationStore] Cleared session: {SessionId}", sessionId);
    }

    // ── Eviction ─────────────────────────────────────────────────────────────

    private void EvictExpired()
    {
        var cutoff = DateTime.UtcNow - SessionTtl;
        var expired = _sessions
            .Where(kv => kv.Value.LastUpdated < cutoff)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in expired)
        {
            if (_sessions.TryRemove(key, out _))
                _logger.LogInformation("[ConversationStore] Evicted expired session: {SessionId}", key);
        }
    }
}
