using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class VisualMacroTests
{
    [Fact]
    public void Compile_keeps_locator_action_wait_and_before_after_expectations()
    {
        var macro = VisualMacroCompiler.Compile(Route(), Revision(), ["spend-premium-currency"]);

        var step = Assert.Single(macro.Steps);
        Assert.Equal("locator:v1", step.LocatorRevision);
        Assert.Equal("click", step.Primitive);
        Assert.Equal(["signature:source"], step.SourceSceneSignatureIds);
        Assert.Equal(["signature:destination"], step.DestinationSceneSignatureIds);
        Assert.Equal(VisualMacroExecutionMode.Supervised, macro.ExecutionMode);
    }

    [Fact]
    public void Compile_rejects_prohibited_risk()
    {
        var revision = Revision();
        revision = revision with
        {
            ScreenGraph = revision.ScreenGraph with
            {
                Edges = [revision.ScreenGraph.Edges[0] with { RiskTags = ["spend-premium-currency"] }],
            },
        };

        Assert.Throws<InvalidOperationException>(() =>
            VisualMacroCompiler.Compile(Route(), revision, ["spend-premium-currency"]));
    }

    [Theory]
    [InlineData(CaptureAvailability.Available, StateIdentityStatus.Known, "state:source", VisualMacroAuditStatus.Confirmed)]
    [InlineData(CaptureAvailability.Available, StateIdentityStatus.Known, "state:other", VisualMacroAuditStatus.UnexpectedState)]
    [InlineData(CaptureAvailability.Available, StateIdentityStatus.Ambiguous, null, VisualMacroAuditStatus.Ambiguous)]
    [InlineData(CaptureAvailability.Unavailable, StateIdentityStatus.Known, "state:source", VisualMacroAuditStatus.Unavailable)]
    [InlineData(CaptureAvailability.Stale, StateIdentityStatus.Known, "state:source", VisualMacroAuditStatus.Stale)]
    public void Audit_before_continues_only_on_confirmed_expected_scene(
        CaptureAvailability availability,
        StateIdentityStatus identity,
        string? hypothesis,
        VisualMacroAuditStatus expected)
    {
        var step = Assert.Single(VisualMacroCompiler.Compile(Route(), Revision(), []).Steps);

        var result = VisualMacroAuditor.AuditBefore(step, Scene(availability, identity, hypothesis));

        Assert.Equal(expected, result.Status);
        Assert.Equal(expected == VisualMacroAuditStatus.Confirmed, result.CanContinue);
    }

    [Fact]
    public void Audit_before_click_requires_the_frame_bound_target_from_the_same_observation()
    {
        var step = Assert.Single(VisualMacroCompiler.Compile(Route(), Revision(), []).Steps);

        var result = VisualMacroAuditor.AuditBefore(
            step,
            Scene(CaptureAvailability.Available, StateIdentityStatus.Known, "state:source", includeTarget: false));

        Assert.Equal(VisualMacroAuditStatus.Ambiguous, result.Status);
        Assert.False(result.CanContinue);
    }

    private static LearningRouteRevision Route() => new(
        ContractSchemaVersions.Revision03,
        "route-1",
        1,
        "route:0123456789abcdef",
        null,
        "nikke",
        "windows11-ja/nikke-live",
        "structure:v1",
        "日課を完了",
        ["edge:open"],
        LearningRouteAuthor.User,
        "この順序で保存",
        "利用者訂正",
        LearningRouteStatus.Compiled,
        DateTimeOffset.UnixEpoch);

    private static GameStructureRevision Revision() => new(
        ContractSchemaVersions.Revision03,
        "structure:v1",
        null,
        1,
        new StructureScreenGraph(
            ContractSchemaVersions.Revision03,
            "graph:v1",
            [Node("state:source"), Node("state:destination")],
            [Edge()],
            [],
            "windows11-ja/nikke-live"),
        [],
        [],
        "windows11-ja/nikke-live",
        DateTimeOffset.UnixEpoch);

    private static StructureScreenNode Node(string id) => new(
        ContractSchemaVersions.Revision03,
        id,
        "windows11-ja/nikke-live",
        [$"signature:{id["state:".Length..]}"],
        [],
        [$"evidence:{id}"],
        id,
        StructureVerificationState.Replayed);

    private static StructureScreenEdge Edge() => new(
        ContractSchemaVersions.Revision03,
        "edge:open",
        "state:source",
        "state:destination",
        null,
        "affordance:daily",
        "locator:v1",
        "click",
        "supervised",
        [],
        true,
        "before:1",
        "after:1",
        new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 300, 10000),
        [new StructureOutcomeCount(ExplorationOutcomeKind.Destination, 2)],
        ["evidence:1"],
        StructureVerificationState.Replayed);

    private static ObservedScene Scene(
        CaptureAvailability availability,
        StateIdentityStatus identity,
        string? hypothesis,
        bool includeTarget = true) => new(
        ContractSchemaVersions.Revision03,
        "scene-1",
        "observation-1",
        new CapturedFrameReference(
            ContractSchemaVersions.Revision03,
            "window-1",
            CaptureBackend.WindowsGraphicsCapture,
            1,
            100,
            DateTimeOffset.UnixEpoch,
            1,
            10,
            10),
        availability,
        identity,
        hypothesis,
        [],
        includeTarget
            ? [new AffordanceCandidate(
                ContractSchemaVersions.Revision03,
                "affordance:daily",
                "observation-1",
                1,
                1,
                "window-1",
                new AffordanceLocator(ContractSchemaVersions.Revision03, "ocr-normalized-rect", [0.1, 0.1, 0.1, 0.1], "locator:v1"),
                [],
                1,
                ["click"])]
            : [],
        "perception:v1");
}
