using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Exploration;
using Xunit;

namespace OpenLogicool.Exploration.Tests;

public sealed class IncrementalKnownScreenIndexTests
{
    [Fact]
    public void Appends_only_discovered_controls_and_links_destination_incrementally()
    {
        var store = new MemoryProfileStore();
        var index = new IncrementalKnownScreenIndex(store, "nikke", "env", "nikke");
        var source = Scene("source", 1, "アークメニュー", "ロストセクター");
        var arena = Control(source, "アリーナ", [0.5, 0.5, 0.1, 0.05]);
        var tower = Control(source, "トライブタワー", [0.6, 0.3, 0.1, 0.05]);
        source = source with { Affordances = [arena, tower] };
        var destination = Scene("destination", 2, "ランキング", "企業別ランキング");

        var first = index.RememberControl(source, arena, "evidence-1");

        var afterFirst = store.Document!;
        var sourceState = Assert.Single(afterFirst.States);
        Assert.DoesNotContain(sourceState.Anchors, anchor => anchor.Text == "LARGE ENGLISH DISPLAY");
        Assert.DoesNotContain(sourceState.Anchors, anchor => anchor.Text == "ロイよNOWFLAKESPASS");
        Assert.DoesNotContain(sourceState.Anchors, anchor => anchor.Text == "ーク");
        Assert.Equal(["アークメニュー", "ロストセクター"], sourceState.Anchors.Select(anchor => anchor.Text));
        var savedArena = Assert.Single(sourceState.Affordances);
        Assert.Equal(first.ActionId, savedArena.CandidateId);
        Assert.Equal(arena.Locator.NormalizedBounds, savedArena.NormalizedBounds);
        Assert.Null(savedArena.DestinationStateId);

        _ = index.RememberControl(source, tower, "evidence-2");
        Assert.Equal(2, Assert.Single(store.Document!.States).Affordances.Count);

        var linked = index.RememberDestination(source, arena, [destination, destination], "evidence-3");

        Assert.NotNull(linked.DestinationStateId);
        Assert.Equal(2, store.Document!.States.Count);
        sourceState = store.Document.States.Single(state => state.StateId == linked.PageStateId);
        savedArena = sourceState.Affordances.Single(action => action.CandidateId == linked.ActionId);
        Assert.Equal(linked.DestinationStateId, savedArena.DestinationStateId);
        Assert.Equal(2, sourceState.Affordances.Count);
        var destinationState = store.Document.States.Single(state => state.StateId == linked.DestinationStateId);
        Assert.Contains(sourceState.StateId, destinationState.SupersedesStateIds!);
    }

    [Fact]
    public void Cleaner_similar_ocr_updates_text_without_changing_state_or_action_ids()
    {
        var store = new MemoryProfileStore();
        var index = new IncrementalKnownScreenIndex(store, "game", "env", "game");
        var noisy = Scene("noisy", 1, "前哨%地", "隊員募%");
        var noisyControl = Control(noisy, "アリ%ナ", [0.5, 0.5, 0.1, 0.05]);
        noisy = noisy with { Affordances = [noisyControl] };
        var first = index.RememberControl(noisy, noisyControl, "evidence-noisy");
        var clean = Scene("clean", 2, "前哨基地", "隊員募集");
        var cleanControl = Control(clean, "アリーナ", [0.5, 0.5, 0.1, 0.05]);
        clean = clean with { Affordances = [cleanControl] };

        var second = index.RememberControl(clean, cleanControl, "evidence-clean");

        Assert.Equal(first.PageStateId, second.PageStateId);
        Assert.Equal(first.ActionId, second.ActionId);
        var state = Assert.Single(store.Document!.States);
        Assert.Contains(state.Anchors, anchor => anchor.Text == "前哨基地"
            && anchor.PreviousTexts!.Contains("前哨%地", StringComparer.Ordinal));
        var action = Assert.Single(state.Affordances);
        Assert.Equal("アリーナ", action.Text);
        Assert.Contains("アリ%ナ", action.PreviousTexts!);
    }

    [Fact]
    public void Destination_with_one_usable_ocr_anchor_is_saved_instead_of_rejected()
    {
        var store = new MemoryProfileStore();
        var index = new IncrementalKnownScreenIndex(store, "game", "env", "game");
        var source = Scene("source", 1, "ロビー", "隊員募集");
        var control = Control(source, "イベント", [0.9, 0.3, 0.05, 0.1]);
        source = source with { Affordances = [control] };
        var destination = Scene("destination", 2, "イベント", "unused");
        destination = destination with
        {
            DiscoveryEvidence = destination.DiscoveryEvidence! with
            {
                LocalGroundingRegions = [Ground("イベント", [0.1, 0.1, 0.2, 0.05])],
            },
        };

        var linked = index.RememberDestination(source, control, [destination], "evidence-destination");

        Assert.NotNull(linked.DestinationStateId);
        var saved = store.Document!.States.Single(state => state.StateId == linked.DestinationStateId);
        Assert.Equal("イベント", Assert.Single(saved.Anchors).Text);
    }

    [Fact]
    public void Destination_without_ocr_anchor_is_saved_by_its_page_visual_patch()
    {
        var store = new MemoryProfileStore();
        var index = new IncrementalKnownScreenIndex(store, "game", "env", "game");
        var source = Scene("source", 1, "デイリー", "ミッション");
        var control = Control(source, "閉じる", [0.9, 0.05, 0.05, 0.05]);
        source = source with { Affordances = [control] };
        var destination = Scene("destination", 2, "A", "B") with
        {
            DiscoveryEvidence = Scene("destination", 2, "A", "B").DiscoveryEvidence! with
            {
                LocalGroundingRegions = [],
            },
            SceneVisualPatch = new VisualPatchSignature(
                ContractSchemaVersions.Revision03,
                2,
                2,
                Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }),
                "page-sha"),
        };

        var linked = index.RememberDestination(source, control, [destination], "visual-destination");

        var saved = store.Document!.States.Single(state => state.StateId == linked.DestinationStateId);
        Assert.Empty(saved.Anchors);
        Assert.Equal("page-sha", saved.VisualPatch?.Sha256);
    }

    [Fact]
    public void Cleaner_ocr_does_not_collapse_existing_same_position_variants()
    {
        var store = new MemoryProfileStore();
        var index = new IncrementalKnownScreenIndex(store, "game", "env", "game");
        var bounds = new double[] { 0.2, 0.2, 0.3, 0.05 };
        var noisy = Scene("noisy", 1, "A", "B") with
        {
            DiscoveryEvidence = Scene("noisy", 1, "A", "B").DiscoveryEvidence! with
            {
                LocalGroundingRegions =
                [
                    Ground("フィィベーンルトド", bounds),
                    Ground("フィイベーンルトド", bounds),
                ],
            },
        };
        var control = Control(noisy, "ショップ", [0.5, 0.5, 0.1, 0.05]);
        noisy = noisy with { Affordances = [control] };
        _ = index.RememberControl(noisy, control, "noisy-evidence");
        var clean = Scene("clean", 2, "イベントフィールド", "ミッション");
        var cleanControl = Control(clean, "ショップ", [0.5, 0.5, 0.1, 0.05]);
        clean = clean with { Affordances = [cleanControl] };

        _ = index.RememberControl(clean, cleanControl, "clean-evidence");

        LearnedSceneProfileValidator.Validate(store.Document!);
        Assert.Equal(2, store.Document!.States[0].Anchors
            .Select(anchor => OcrTextMatcher.Normalize(anchor.Text)).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Existing_duplicate_state_ids_are_canonicalized_when_the_page_is_seen_again()
    {
        var store = new MemoryProfileStore();
        var index = new IncrementalKnownScreenIndex(store, "game", "env", "game");
        var scene = Scene("source", 1, "ロビー", "隊員募集");
        var control = Control(scene, "ショップ", [0.5, 0.5, 0.1, 0.05]);
        scene = scene with { Affordances = [control] };
        _ = index.RememberControl(scene, control, "first");
        var state = Assert.Single(store.Document!.States);
        store.Seed(store.Document with { States = [state, state] });

        _ = index.RememberControl(scene, control, "second");

        Assert.Single(store.Document!.States);
        LearnedSceneProfileValidator.Validate(store.Document);
    }

    private static ObservedScene Scene(string id, long sequence, string anchor1, string anchor2)
    {
        var frame = new CapturedFrameReference(
            ContractSchemaVersions.Revision03,
            "window:nikke",
            CaptureBackend.WindowsGraphicsCapture,
            sequence,
            sequence * 100,
            DateTimeOffset.UnixEpoch,
            1,
            0,
            0);
        return new ObservedScene(
            ContractSchemaVersions.Revision03,
            $"scene:{id}",
            $"observation:{id}",
            frame,
            CaptureAvailability.Available,
            StateIdentityStatus.Novel,
            null,
            [],
            [],
            "foundry-local",
            new SceneDiscoveryEvidence(
                "foundry-local",
                "model",
                "prompt",
                "sha",
                "Completed",
                "None",
                null,
                "{}",
                1,
                0,
                0,
                LocalGroundingRegions:
                [
                    Ground(anchor1, [0.2, 0.2, 0.3, 0.05]),
                    Ground(anchor2, [0.6, 0.6, 0.25, 0.04]),
                    Ground("LARGE ENGLISH DISPLAY", [0.1, 0.4, 0.7, 0.10]),
                    Ground("ロイよNOWFLAKESPASS", [0.8, 0.1, 0.18, 0.12]),
                    Ground("ーク", [0.21, 0.2, 0.15, 0.05]),
                ]));
    }

    private static SceneGroundingRegion Ground(string text, IReadOnlyList<double> bounds) =>
        new(text, new EvidenceRegion(ContractSchemaVersions.Revision03, "rect", bounds, "windows-ocr"));

    private static AffordanceCandidate Control(
        ObservedScene scene,
        string label,
        IReadOnlyList<double> bounds) =>
        new(
            ContractSchemaVersions.Revision03,
            $"candidate:{label}",
            scene.ObservationId,
            scene.Frame.Sequence,
            scene.Frame.TransformRevision,
            scene.Frame.SourceId,
            new AffordanceLocator(
                ContractSchemaVersions.Revision03,
                "ocr-text-region",
                bounds,
                $"locator:{label}"),
            [new EvidenceRegion(ContractSchemaVersions.Revision03, "rect", bounds, "windows-ocr")],
            1,
            [GameInteractionOperations.Click],
            "text",
            label);

    private sealed class MemoryProfileStore : ILearnedSceneProfileStore
    {
        public LearnedSceneProfileDocument? Document { get; private set; }

        public void Seed(LearnedSceneProfileDocument document) => Document = document;

        public void Upsert(LearnedSceneProfileDocument document)
        {
            LearnedSceneProfileValidator.Validate(document);
            Document = document;
        }

        public LearnedSceneProfileDocument? Load(string gameId, string environmentScope) => Document;
    }
}
