using HistoricalGraphExplorer.Domain;

namespace HistoricalGraphExplorer.Application.Interfaces;

/// <summary>
/// Per-session structured memory store.
/// In-process implementation uses ConcurrentDictionary.
/// Swap for Redis implementation in production.
/// </summary>
public interface IConversationStore
{
    ConversationState GetOrCreate(string sessionId);
    void Save(string sessionId, ConversationState state);
    void Clear(string sessionId);
}
