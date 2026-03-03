namespace HistoricalGraphExplorer.Application.Interfaces;

public interface IGraphCopilotService
{
    Task<string> AskAsync(string question);
}
