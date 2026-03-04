namespace HistoricalGraphExplorer.Application.Interfaces;

public interface IGraphCopilotService
{
    /// <summary>
    /// Answer a question, optionally within a conversation session.
    /// Pass a stable sessionId to enable multi-turn memory.
    /// </summary>
    Task<string> AskAsync(string question, string? sessionId = null);
}
