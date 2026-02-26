using FluentAssertions;
using HistoricalGraphExplorer.Application.Interfaces;
using HistoricalGraphExplorer.Application.Services;
using Microsoft.SemanticKernel;
using NSubstitute;

namespace HistoricalGraphExplorer.Tests;

public class GraphCopilotTests
{
    private readonly IGremlinRepository _repo = Substitute.For<IGremlinRepository>();

    [Fact]
    public async Task AskAsync_UnsafeQuery_ReturnsRejectionMessage()
    {
        // Arrange
        var kernel = CreateKernelThatReturns("g.V().drop()");
        var service = new GraphCopilotService(kernel, _repo);

        // Act
        var result = await service.AskAsync("Delete all vertices");

        // Assert
        result.Should().Be("Unsafe query rejected.");
        await _repo.DidNotReceive().ExecuteAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task AskAsync_SafeQuery_CallsRepository()
    {
        // Arrange
        var safeGremlin = "g.V().has('name','Ottoman Empire')";
        var kernel = CreateKernelThatReturns(safeGremlin, "Some answer");
        _repo.ExecuteAsync(Arg.Any<string>()).Returns(Array.Empty<dynamic>());

        var service = new GraphCopilotService(kernel, _repo);

        // Act
        var result = await service.AskAsync("What is the Ottoman Empire?");

        // Assert
        await _repo.Received(1).ExecuteAsync(Arg.Any<string>());
        result.Should().Be("Some answer");
    }

    // Helper: creates a Kernel stub that returns successive values per InvokePromptAsync call
    private static Kernel CreateKernelThatReturns(params string[] responses)
    {
        // We cannot easily mock Kernel (sealed), so we use a real builder with a fake chat service.
        // For unit tests, inject the copilot with interfaces instead.
        // This test demonstrates NSubstitute usage on the repository boundary.
        throw new NotImplementedException("Replace Kernel with an IKernelWrapper interface for full unit testing.");
    }
}
