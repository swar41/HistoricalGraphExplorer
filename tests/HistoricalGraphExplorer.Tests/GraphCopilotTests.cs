using FluentAssertions;
using HistoricalGraphExplorer.Application.Interfaces;
using HistoricalGraphExplorer.Application.Services;
using HistoricalGraphExplorer.Domain;
using NSubstitute;

namespace HistoricalGraphExplorer.Tests;

public class GraphCopilotTests
{
    private readonly IGraphTraversalService _traversal = Substitute.For<IGraphTraversalService>();
    private readonly IConversationStore     _store     = Substitute.For<IConversationStore>();

    // ── HybridRouter — year range patterns ──────────────────────────────────

    [Theory]
    [InlineData("battles between 1600 and 1700", QueryIntentType.YearRangeQuery)]
    [InlineData("events from 1400 to 1500",      QueryIntentType.YearRangeQuery)]
    [InlineData("wars after 1800",               QueryIntentType.YearRangeQuery)]
    [InlineData("events before 500",             QueryIntentType.YearRangeQuery)]
    [InlineData("events in the 1700s",           QueryIntentType.YearRangeQuery)]
    public void HybridRouter_YearPatterns_ResolveCorrectly(string question, QueryIntentType expected)
    {
        var result = HybridRouter.TryResolve(question);
        result.Should().NotBeNull();
        result!.Intent.Should().Be(expected);
    }

    [Fact]
    public void HybridRouter_BetweenYears_ExtractsCorrectBounds()
    {
        var result = HybridRouter.TryResolve("battles between 1600 and 1700");
        result!.FromYear.Should().Be(1600);
        result.ToYear.Should().Be(1700);
    }

    [Fact]
    public void HybridRouter_AfterYear_ExtractsFromYear()
    {
        var result = HybridRouter.TryResolve("events after 1800");
        result!.FromYear.Should().Be(1800);
        result.ToYear.Should().BeNull();
    }

    [Fact]
    public void HybridRouter_UnknownQuestion_ReturnsNull()
    {
        var result = HybridRouter.TryResolve("Tell me about the world");
        result.Should().BeNull();
    }

    // ── ContextResolver — pronoun rewriting ──────────────────────────────────

    [Fact]
    public void ContextResolver_NoState_ReturnsOriginalQuestion()
    {
        var result = ContextResolver.Resolve("Which of them were after 1700?", null);
        result.Should().Be("Which of them were after 1700?");
    }

    [Fact]
    public void ContextResolver_WithParticipantState_RewritesPronouns()
    {
        var state = new ConversationState { LastParticipant = "ottoman-empire" };
        var result = ContextResolver.Resolve("Which of them were in Europe?", state);
        result.Should().Contain("ottoman-empire");
    }

    [Fact]
    public void ContextResolver_NonContextualQuestion_NotRewritten()
    {
        var state = new ConversationState { LastParticipant = "ottoman-empire" };
        var result = ContextResolver.Resolve("What events happened in France?", state);
        result.Should().Be("What events happened in France?");
    }

    // ── Fix #1: In-memory filter — year only, NO place filter ────────────────

    [Fact]
    public void ContextResolver_TryFilterInMemory_FiltersAfterYear_FromBaseEvents()
    {
        var state = new ConversationState
        {
            LastParticipant = "ottoman-empire",
            BaseEvents = new List<EventVertex>
            {
                new("wd:Q1", "Battle A", null, 1650),
                new("wd:Q2", "Battle B", null, 1720),
                new("wd:Q3", "Battle C", null, 1800),
            }
        };

        var result = ContextResolver.TryFilterInMemory("Which of them were after 1700?", state);
        result.Should().NotBeNull();
        result!.Count.Should().Be(2);
        result.All(e => e.StartYear > 1700).Should().BeTrue();
    }

    [Fact]
    public void ContextResolver_TryFilterInMemory_FiltersBetweenYears()
    {
        var state = new ConversationState
        {
            LastParticipant = "ottoman-empire",
            BaseEvents = new List<EventVertex>
            {
                new("wd:Q1", "Battle A", null, 1600),
                new("wd:Q2", "Battle B", null, 1650),
                new("wd:Q3", "Battle C", null, 1750),
            }
        };

        var result = ContextResolver.TryFilterInMemory("Which of them were between 1620 and 1700?", state);
        result.Should().NotBeNull();
        result!.Count.Should().Be(1);
        result.Single().Name.Should().Be("Battle B");
    }

    [Fact]
    public void ContextResolver_TryFilterInMemory_PlaceFollowUp_ReturnsNull()
    {
        // Fix #1: place-based follow-up must NOT be filtered in-memory
        var state = new ConversationState
        {
            LastParticipant = "ottoman-empire",
            BaseEvents = new List<EventVertex>
            {
                new("wd:Q1", "Battle of Ankara", null, 1402),
                new("wd:Q2", "Siege of Vienna",  null, 1683),
            }
        };

        // "in Europe" should trigger DB query, not name.Contains("europe")
        var result = ContextResolver.TryFilterInMemory("Which of them were in Europe?", state);
        result.Should().BeNull("place-based follow-ups must route to DB, not in-memory filter");
    }

    [Fact]
    public void ContextResolver_TryFilterInMemory_NoBaseEvents_ReturnsNull()
    {
        var state = new ConversationState(); // no BaseEvents
        var result = ContextResolver.TryFilterInMemory("Which of them were after 1700?", state);
        result.Should().BeNull();
    }

    // ── Fix #4: BaseEvents preserved across follow-ups (no cascading narrowing) ─

    [Fact]
    public void ConversationState_BaseEvents_NotOverwrittenByFollowUpFilter()
    {
        // Simulate: Q1 fetches 3 events → stored in BaseEvents
        var state = new ConversationState
        {
            LastParticipant = "ottoman-empire",
            BaseEvents = new List<EventVertex>
            {
                new("wd:Q1", "Battle A", null, 1600),
                new("wd:Q2", "Battle B", null, 1720),
                new("wd:Q3", "Battle C", null, 1800),
            }
        };

        // Q2 filter: after 1700 → gets 2 results from BaseEvents
        var q2Result = ContextResolver.TryFilterInMemory("Which of them were after 1700?", state);
        q2Result!.Count.Should().Be(2);

        // BaseEvents still has all 3 — NOT reduced to q2Result
        state.BaseEvents.Count.Should().Be(3, "BaseEvents must never be overwritten by a follow-up year filter");

        // Q3 filter: before 1750 → should still get 2 results (1600 + 1720), not just 1720
        var q3Result = ContextResolver.TryFilterInMemory("Which of them were before 1750?", state);
        q3Result!.Count.Should().Be(2, "Q3 filters BaseEvents, not Q2's filtered subset");
    }

    // ── Fix #2: HasContext auto-resets stale state ────────────────────────────

    [Fact]
    public void ConversationState_HasContext_ReturnsFalse_WhenStale()
    {
        var state = new ConversationState
        {
            LastParticipant = "ottoman-empire",
            LastUpdated = DateTime.UtcNow.AddMinutes(-31) // older than 30 min TTL
        };

        state.HasContext.Should().BeFalse("stale session should auto-reset and return false");
        state.LastParticipant.Should().BeNull("Reset() should have cleared all fields");
    }

    [Fact]
    public void ConversationState_HasContext_ReturnsTrue_WhenFresh()
    {
        var state = new ConversationState
        {
            LastParticipant = "ottoman-empire",
            LastUpdated = DateTime.UtcNow
        };

        state.HasContext.Should().BeTrue();
    }

    [Fact]
    public void ConversationState_Reset_ClearsAllFields()
    {
        var state = new ConversationState
        {
            LastParticipant = "ottoman-empire",
            LastPlace = "ukraine",
            LastFromYear = 1600,
            LastToYear = 1700,
            BaseEvents = new List<EventVertex> { new("wd:Q1", "X", null, 1650) },
            LastEvents = new List<EventVertex> { new("wd:Q1", "X", null, 1650) },
        };

        state.Reset();

        state.LastParticipant.Should().BeNull();
        state.LastPlace.Should().BeNull();
        state.LastFromYear.Should().BeNull();
        state.LastToYear.Should().BeNull();
        state.BaseEvents.Should().BeEmpty();
        state.LastEvents.Should().BeEmpty();
    }

    // ── Interface contract tests ──────────────────────────────────────────────

    [Fact]
    public void IGraphTraversalService_ReturnsTypedCollections()
    {
        var t = typeof(IGraphTraversalService);

        t.GetMethod(nameof(IGraphTraversalService.GetEventsByParticipantAsync))!
         .ReturnType.Should().Be(typeof(Task<IReadOnlyCollection<EventVertex>>));

        t.GetMethod(nameof(IGraphTraversalService.GetEventsByPlaceAsync))!
         .ReturnType.Should().Be(typeof(Task<IReadOnlyCollection<EventVertex>>));

        t.GetMethod(nameof(IGraphTraversalService.GetEventsByYearRangeAsync))!
         .ReturnType.Should().Be(typeof(Task<IReadOnlyCollection<EventVertex>>));
    }

    [Fact]
    public void IGraphCopilotService_AskAsync_AcceptsOptionalSessionId()
    {
        var m = typeof(IGraphCopilotService).GetMethod(nameof(IGraphCopilotService.AskAsync))!;
        var parameters = m.GetParameters();
        parameters.Should().HaveCount(2);
        parameters[1].Name.Should().Be("sessionId");
        parameters[1].IsOptional.Should().BeTrue();
    }
}
