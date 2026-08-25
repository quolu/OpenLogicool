using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Exploration;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class WindowsKnownFirstTargetDiscoveryTests
{
    [Fact]
    public async Task Saved_action_is_returned_without_ai_discovery()
    {
        var ai = new AiDiscovery();
        var discovery = Discovery(new ProfileStore(Profile()), ai);
        var frame = Frame();

        var scene = await discovery.DiscoverAsync(Observation(frame), frame);

        Assert.Equal(StateIdentityStatus.Known, scene.StateIdentity);
        Assert.Equal("state:lobby", scene.StateHypothesisId);
        Assert.Equal("affordance:squad", Assert.Single(scene.Affordances).CandidateId);
        Assert.Equal(0, ai.CallCount);
    }

    [Fact]
    public async Task Missing_saved_data_uses_ai_once()
    {
        var ai = new AiDiscovery();
        var discovery = Discovery(new ProfileStore(null), ai);
        var frame = Frame();

        var scene = await discovery.DiscoverAsync(Observation(frame), frame);

        Assert.Equal("ai-target", Assert.Single(scene.Affordances).CandidateId);
        Assert.Equal(1, ai.CallCount);
    }

    [Fact]
    public async Task Saved_action_with_unconfirmed_transition_uses_ai_on_next_observation()
    {
        var ai = new AiDiscovery();
        var discovery = Discovery(new ProfileStore(Profile()), ai);
        var frame = Frame();
        var observation = Observation(frame);
        var known = await discovery.DiscoverAsync(observation, frame);

        discovery.MarkTransitionUnconfirmed(known, Assert.Single(known.Affordances));
        var rediscovered = await discovery.DiscoverAsync(observation, frame);

        Assert.Equal("ai-target", Assert.Single(rediscovered.Affordances).CandidateId);
        Assert.Equal(1, ai.CallCount);
    }

    [Fact]
    public async Task Failed_saved_action_stays_on_ai_repair_until_a_moved_result()
    {
        var ai = new AiDiscovery();
        var discovery = Discovery(new ProfileStore(Profile()), ai);
        var frame = Frame();
        var observation = Observation(frame);
        var known = await discovery.DiscoverAsync(observation, frame);

        discovery.MarkTransitionUnconfirmed(known, Assert.Single(known.Affordances));
        _ = await discovery.DiscoverAsync(observation, frame);
        _ = await discovery.DiscoverAsync(observation, frame);

        Assert.Equal(2, ai.CallCount);
    }

    [Fact]
    public async Task Non_moving_ai_action_on_a_novel_page_forces_ai_repair_on_the_next_attempt()
    {
        var ai = new AiDiscovery();
        var discovery = Discovery(new ProfileStore(null), ai);
        var frame = Frame();
        var first = await discovery.DiscoverAsync(Observation(frame), frame);

        discovery.MarkTransitionUnconfirmed(first, Assert.Single(first.Affordances));
        _ = await discovery.DiscoverAsync(Observation(frame), frame);

        Assert.Equal(2, ai.CallCount);
    }

    [Fact]
    public async Task Comparison_observation_never_starts_next_step_ai_discovery()
    {
        var ai = new AiDiscovery();
        var discovery = Discovery(new ProfileStore(null), ai);
        var frame = Frame();

        discovery.BeginComparison();
        var local = await discovery.DiscoverAsync(Observation(frame), frame);
        discovery.EndComparison();

        Assert.NotEmpty(local.Affordances);
        Assert.NotEmpty(local.DiscoveryEvidence!.LocalGroundingRegions!);
        Assert.NotNull(local.SceneVisualPatch);
        Assert.Equal(0, ai.CallCount);
        _ = await discovery.DiscoverAsync(Observation(frame), frame);
        Assert.Equal(1, ai.CallCount);
    }

    [Fact]
    public async Task Route_target_hint_selects_the_saved_step_without_goal_text_similarity()
    {
        var ai = new AiDiscovery();
        var discovery = Discovery(new ProfileStore(ProfileWithTwoActions()), ai);
        var frame = Frame();
        discovery.SetRouteTarget(RouteEdge("フレンド", [0.20, 0.89, 0.06, 0.03]));

        var scene = await discovery.DiscoverAsync(Observation(frame), frame);

        var target = Assert.Single(scene.Affordances);
        Assert.StartsWith("route:", target.CandidateId, StringComparison.Ordinal);
        Assert.Equal("フレンド", target.SemanticLabel);
        Assert.Equal([0.20, 0.89, 0.06, 0.03], target.Locator.NormalizedBounds);
        Assert.Equal(0, ai.CallCount);
    }

    [Fact]
    public async Task Route_target_uses_saved_coordinates_without_a_known_screen_profile_or_ai()
    {
        var ai = new AiDiscovery();
        var discovery = Discovery(new ProfileStore(null), ai);
        var frame = Frame();
        discovery.SetRouteTarget(RouteEdge("アーク", [0.53, 0.63, 0.15, 0.15]));

        var scene = await discovery.DiscoverAsync(Observation(frame), frame);

        var target = Assert.Single(scene.Affordances);
        Assert.Equal("アーク", target.SemanticLabel);
        Assert.Equal([0.53, 0.63, 0.15, 0.15], target.Locator.NormalizedBounds);
        Assert.Equal(0, ai.CallCount);
    }

    [Fact]
    public async Task Destination_unknown_never_blocks_the_goal_less_saved_action_fallback()
    {
        var profile = Profile();
        var state = profile.States[0];
        profile = profile with
        {
            States = [state with
            {
                Affordances = [state.Affordances[0] with { DestinationStateId = null }],
            }],
        };
        var ai = new AiDiscovery();
        var discovery = Discovery(new ProfileStore(profile), ai, goal: null);
        var frame = Frame();

        var scene = await discovery.DiscoverAsync(Observation(frame), frame);

        Assert.Equal("affordance:squad", Assert.Single(scene.Affordances).CandidateId);
        Assert.Equal(0, ai.CallCount);
    }

    private static WindowsKnownFirstTargetDiscovery Discovery(
        ILearnedSceneProfileStore profiles,
        IProductGameTargetDiscovery ai,
        string? goal = "部隊を開く") => new(
        ai,
        new Ocr(),
        profiles,
        "game",
        "env",
        goal,
        GameInteractionOperations.Click);

    private static LearnedSceneProfileDocument Profile() => new(
        ContractSchemaVersions.Revision03,
        "profile:1",
        "known-screen-index-v1",
        "game",
        "env",
        "game",
        null,
        1_000,
        0.04,
        [new LearnedStateSceneSignature(
            "state:lobby",
            "signature:lobby",
            [
                new LearnedSceneAnchor("ロビー", [0.50, 0.89, 0.06, 0.03], "e1"),
                new LearnedSceneAnchor("隊員募集", [0.64, 0.89, 0.09, 0.03], "e2"),
            ],
            [new LearnedAffordanceSignature(
                "affordance:squad",
                "locator:squad:v1",
                "部隊",
                [0.44, 0.89, 0.05, 0.03],
                [GameInteractionOperations.Click],
                ["e3"],
                "state:squad")],
            ["e1", "e2"])],
        ["profile-evidence"]);

    private static LearnedSceneProfileDocument ProfileWithTwoActions()
    {
        var profile = Profile();
        var state = profile.States[0];
        return profile with
        {
            States = [state with
            {
                Affordances =
                [
                    .. state.Affordances,
                    new LearnedAffordanceSignature(
                        "affordance:friend",
                        "locator:friend:v1",
                        "フレンド",
                        [0.20, 0.89, 0.06, 0.03],
                        [GameInteractionOperations.Click],
                        ["e4"],
                        "state:friend"),
                ],
            }],
        };
    }

    private static StructureScreenEdge RouteEdge(string label, IReadOnlyList<double> bounds) => new(
        ContractSchemaVersions.Revision03,
        "edge:route",
        "source",
        "destination",
        null,
        "original-candidate",
        "old-locator",
        GameInteractionOperations.Click,
        "goal-route",
        [],
        false,
        "before",
        "after",
        new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 1_000, 10_000),
        [new StructureOutcomeCount(ExplorationOutcomeKind.Destination, 1)],
        ["evidence"],
        StructureVerificationState.Candidate,
        TargetSemanticKey: GameSceneSemanticComparer.TargetKey("text", label, bounds),
        TargetNormalizedBounds: bounds);

    private static CapturedFrame Frame()
    {
        const int width = 1_000;
        const int height = 1_000;
        return new CapturedFrame(
            ContractSchemaVersions.Revision03,
            "window:game",
            CaptureBackend.WindowsGraphicsCapture,
            1,
            1_000,
            DateTimeOffset.UnixEpoch,
            width,
            height,
            "B8G8R8A8_UNorm",
            96,
            96,
            1,
            10,
            250,
            Pixels: new FramePixels(new byte[width * height * 4], width * 4));
    }

    private static ObservationResult Observation(CapturedFrame frame) => new(
        ContractSchemaVersions.Revision03,
        "observation:current",
        new CapturedFrameReference(
            frame.SchemaVersion,
            frame.SourceId,
            frame.Backend,
            frame.Sequence,
            frame.MonotonicMs,
            frame.WallClockUtc,
            frame.TransformRevision,
            frame.FreshnessMs,
            frame.LastChangeMs),
        CaptureAvailability.Available,
        StateIdentityStatus.Novel,
        [],
        "zero-seed",
        frame.FreshnessMs,
        null);

    private sealed class Ocr : IWindowsGameOcrRecognizer
    {
        public ValueTask<WindowsGameOcrResult> RecognizeAsync(
            CapturedFrame frame,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(new WindowsGameOcrResult(
            "ロビー 隊員募集 部隊",
            "ja",
            1,
            [
                new WindowsGameOcrWord("ロビー", 500, 890, 60, 30),
                new WindowsGameOcrWord("隊員募集", 640, 890, 90, 30),
                new WindowsGameOcrWord("部隊", 440, 890, 50, 30),
                new WindowsGameOcrWord("フレンド", 200, 890, 60, 30),
            ]));
    }

    private sealed class ProfileStore(LearnedSceneProfileDocument? profile) : ILearnedSceneProfileStore
    {
        private LearnedSceneProfileDocument? current = profile;
        public void Upsert(LearnedSceneProfileDocument document) => current = document;
        public LearnedSceneProfileDocument? Load(string gameId, string environmentScope) => current;
    }

    private sealed class AiDiscovery : IProductGameTargetDiscovery
    {
        public int CallCount { get; private set; }

        public ValueTask<ObservedScene> DiscoverAsync(
            ObservationResult observation,
            CapturedFrame frame,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            var candidate = new AffordanceCandidate(
                ContractSchemaVersions.Revision03,
                "ai-target",
                observation.ObservationId,
                observation.Frame.Sequence,
                observation.Frame.TransformRevision,
                observation.Frame.SourceId,
                new AffordanceLocator(
                    ContractSchemaVersions.Revision03,
                    "foundry-local-text-region",
                    [0.4, 0.4, 0.1, 0.05],
                    "ai-locator"),
                [new EvidenceRegion(
                    ContractSchemaVersions.Revision03,
                    "rect",
                    [0.4, 0.4, 0.1, 0.05],
                    "ai")],
                1,
                [GameInteractionOperations.Click],
                "text",
                "部隊");
            return ValueTask.FromResult(new ObservedScene(
                ContractSchemaVersions.Revision03,
                "scene:ai",
                observation.ObservationId,
                observation.Frame,
                observation.CaptureAvailability,
                StateIdentityStatus.Novel,
                null,
                [],
                [candidate],
                "ai"));
        }
    }
}
