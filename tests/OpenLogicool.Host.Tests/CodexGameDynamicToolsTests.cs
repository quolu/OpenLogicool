using System.Text.Json;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class CodexGameDynamicToolsTests
{
    [Fact]
    public async Task Observe_exposes_saved_route_action_and_click_is_bound_to_that_observation()
    {
        var runtime = new Runtime();
        var route = new Route { NextSavedEdge = Edge("saved") };
        var tools = new CodexGameDynamicTools(runtime, route);

        var observed = await tools.ExecuteAsync("observe", Args("{}"));
        var stale = await tools.ExecuteAsync("click", Args(
            "{\"observationId\":\"old\",\"label\":\"MISSION\",\"x\":0.9,\"y\":0.1}"));
        var saved = await tools.ExecuteAsync("use_saved_action", Args(
            "{\"observationId\":\"observation-1\",\"edgeId\":\"saved\"}"));

        Assert.True(observed.Success);
        Assert.NotNull(observed.ImageDataUrl);
        Assert.Contains("saved", observed.Text, StringComparison.Ordinal);
        Assert.False(stale.Success);
        Assert.Contains("束縛", Args(stale.Text).GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.True(saved.Success);
        Assert.True(route.RecordedSaved);
        Assert.Equal("saved", runtime.Commands.Single().SavedEdge!.EdgeId);
    }

    [Fact]
    public async Task New_scroll_records_outcome_and_finish_returns_deduplicated_facts()
    {
        var runtime = new Runtime();
        var route = new Route();
        var tools = new CodexGameDynamicTools(runtime, route);
        _ = await tools.ExecuteAsync("observe", Args("{}"));

        var scroll = await tools.ExecuteAsync("scroll", Args(
            "{\"observationId\":\"observation-1\",\"label\":\"daily list\",\"x\":0.5,\"y\":0.6,\"verticalSteps\":-6}"));
        var finish = await tools.ExecuteAsync("finish", Args(
            "{\"summary\":\"日課情報\",\"facts\":[\"0/100\",\"0/100\",\"派遣 0/3\"]}"));

        Assert.True(scroll.Success);
        Assert.Equal(GameInteractionOperations.Scroll, runtime.Commands.Single().Operation);
        Assert.Equal(-6, runtime.Commands.Single().VerticalScrollSteps);
        Assert.True(route.RecordedNew);
        Assert.True(finish.Success);
        Assert.True(tools.IsCompleted);
        Assert.Equal(["0/100", "派遣 0/3"], tools.FinalFacts);
        Assert.True(route.Completed);
    }

    private static JsonElement Args(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static StructureScreenEdge Edge(string id) => new(
        ContractSchemaVersions.Revision03,
        id,
        "source",
        "destination",
        null,
        "candidate",
        "locator",
        GameInteractionOperations.Click,
        "goal",
        [],
        false,
        "before",
        "after",
        new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 1_000, 10_000),
        [new StructureOutcomeCount(ExplorationOutcomeKind.Destination, 1)],
        ["evidence"],
        StructureVerificationState.Candidate,
        TargetSemanticKey: "icon|MISSION|0|0",
        TargetNormalizedBounds: [0.8, 0.1, 0.1, 0.1]);

    private sealed class Runtime : ICodexGameToolRuntime
    {
        public List<CodexGameActionCommand> Commands { get; } = [];
        public ValueTask<CodexGameObservation> ObserveAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new CodexGameObservation(
                "observation-1",
                "data:image/png;base64,AQ==",
                ["MISSION"],
                []));
        public ValueTask<CodexGameActionOutcome> ExecuteAsync(
            CodexGameActionCommand command,
            bool repairing,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return ValueTask.FromResult(new CodexGameActionOutcome(
                "Learned",
                GameTransitionJudgement.Moved,
                command.SavedEdge?.EdgeId ?? "new-edge",
                "Moved"));
        }
    }

    private sealed class Route : ICodexRouteRecorder
    {
        public StructureScreenEdge? NextSavedEdge { get; set; }
        public int StepNumber { get; private set; }
        public long RevisionNumber { get; private set; }
        public bool Repairing { get; private set; }
        public bool RecordedSaved { get; private set; }
        public bool RecordedNew { get; private set; }
        public bool Completed { get; private set; }
        public void Record(CodexGameActionOutcome outcome, bool usedSavedEdge)
        {
            RecordedSaved |= usedSavedEdge;
            RecordedNew |= !usedSavedEdge;
            RevisionNumber++;
            StepNumber++;
            NextSavedEdge = null;
        }
        public void Complete(IReadOnlyList<string> facts) => Completed = true;
    }
}
