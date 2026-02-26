using HistoricalGraphExplorer.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class IndexModel : PageModel
{
    private readonly IGraphCopilotService _copilot;

    public IndexModel(IGraphCopilotService copilot) => _copilot = copilot;

    [BindProperty]
    public string Question { get; set; } = string.Empty;

    public string? Answer { get; set; }

    public async Task OnPostAsync()
    {
        if (!string.IsNullOrWhiteSpace(Question))
            Answer = await _copilot.AskAsync(Question);
    }
}
