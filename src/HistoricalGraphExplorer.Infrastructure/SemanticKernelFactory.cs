using Microsoft.SemanticKernel;

namespace HistoricalGraphExplorer.Infrastructure;

public static class SemanticKernelFactory
{
    public static Kernel Create(string endpoint, string key, string deployment)
    {
        var builder = Kernel.CreateBuilder();
        builder.AddAzureOpenAIChatCompletion(deployment, endpoint, key);
        return builder.Build();
    }
}
