using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Perception;
using Xunit;

namespace OpenLogicool.Perception.Tests;

public sealed class LearnedSceneMatcherTests
{
    [Fact]
    public void Two_independent_anchors_identify_state_and_rebind_affordance()
    {
        var scene = LearnedSceneMatcher.Match(Profile(), Frame(), new OcrFrameSnapshot(
            "windows-ocr:v1", "ja",
            [
                new OcrWordBox("ロ", 500, 900, 15, 20),
                new OcrWordBox("ビー", 516, 900, 35, 20),
                new OcrWordBox("隊員募集", 650, 900, 80, 20),
                new OcrWordBox("部隊", 450, 900, 40, 20),
            ]));

        Assert.Equal(StateIdentityStatus.Known, scene.StateIdentity);
        Assert.Equal("state:lobby", scene.StateHypothesisId);
        Assert.Equal(2, Assert.Single(scene.StateCandidates).EvidenceRegions.Count);
        var target = Assert.Single(scene.Affordances);
        Assert.Equal("affordance:squad", target.CandidateId);
        Assert.Equal("locator:squad:v1", target.Locator.LocatorRevision);
        Assert.Equal("click", Assert.Single(target.AllowedPrimitives));
    }

    [Fact]
    public void Text_at_wrong_position_is_not_a_state_match()
    {
        var scene = LearnedSceneMatcher.Match(Profile(), Frame(), new OcrFrameSnapshot(
            "windows-ocr:v1", "ja",
            [new OcrWordBox("ロビー", 10, 10, 50, 20), new OcrWordBox("隊員募集", 100, 10, 80, 20)]));

        Assert.Equal(StateIdentityStatus.Novel, scene.StateIdentity);
        Assert.Null(scene.StateHypothesisId);
        Assert.Empty(scene.Affordances);
    }

    [Fact]
    public void Profile_rejects_less_than_two_anchors()
    {
        var invalid = Profile() with
        {
            States = [Profile().States[0] with { Anchors = [Profile().States[0].Anchors[0]] }],
        };

        Assert.Throws<ArgumentException>(() => LearnedSceneProfileValidator.Validate(invalid));
    }

    [Fact]
    public void More_specific_state_can_explicitly_supersede_shared_chrome_state()
    {
        var baseState = Profile().States[0] with { StateId = "state:base" };
        var specific = baseState with
        {
            StateId = "state:specific",
            SupersedesStateIds = ["state:base"],
        };
        var profile = Profile() with { States = [baseState, specific] };

        var scene = LearnedSceneMatcher.Match(profile, Frame(), new OcrFrameSnapshot(
            "windows-ocr:v1", "ja",
            [
                new OcrWordBox("ロビー", 500, 900, 50, 20),
                new OcrWordBox("隊員募集", 650, 900, 80, 20),
                new OcrWordBox("部隊", 450, 900, 40, 20),
            ]));

        Assert.Equal(StateIdentityStatus.Known, scene.StateIdentity);
        Assert.Equal("state:specific", scene.StateHypothesisId);
        Assert.Equal(2, scene.StateCandidates.Count);
    }

    [Fact]
    public void Live_nikke_lobby_tokens_match_the_saved_normalized_anchor_positions()
    {
        var profile = new LearnedSceneProfileDocument(
            "0.3.0", "profile:nikke", "profile:nikke:v1", "nikke", "env", "nikke", "NIKKE", 500, 0.03,
            [new LearnedStateSceneSignature(
                "state:lobby", "signature:lobby",
                [
                    new LearnedSceneAnchor("ロビー", [0.48989, 0.955305, 0.020037, 0.015038], "e1"),
                    new LearnedSceneAnchor("隊員募集", [0.570772, 0.95614, 0.02739, 0.015038], "e2"),
                ], [], ["e1", "e2"])], ["profile-evidence"]);
        var frame = new CapturedFrame(
            "0.3.0", "window:nikke", CaptureBackend.WindowsGraphicsCapture, 1, 1_000,
            DateTimeOffset.UnixEpoch, 2720, 1197, "B8G8R8A8_UNorm", 96, 96, 1, 0, 0);
        var ocr = new OcrFrameSnapshot(
            "windows-ocr:v1", "ja",
            [
                new OcrWordBox("ロ", 1332.5, 1146.5, 15, 14.5),
                new OcrWordBox("ビ", 1352.5, 1143.5, 16, 18),
                new OcrWordBox("ー", 1369.5, 1151.5, 17.5, 3.5),
                new OcrWordBox("隊", 1552.5, 1144.5, 17.5, 18),
                new OcrWordBox("員", 1572, 1145, 16.5, 17.5),
                new OcrWordBox("募", 1591, 1144.5, 17, 18),
                new OcrWordBox("集", 1609.5, 1144.5, 17.5, 18),
            ]);

        var scene = LearnedSceneMatcher.Match(profile, frame, ocr);

        Assert.Equal(StateIdentityStatus.Known, scene.StateIdentity);
        Assert.Equal("state:lobby", scene.StateHypothesisId);
    }

    [Fact]
    public void Visual_affordance_patch_identifies_state_when_ocr_anchors_are_unstable()
    {
        var frame = PixelFrame(40);
        var bounds = new[] { 0.55, 0.55, 0.20, 0.20 };
        var patch = VisualPatchMatcher.Capture(frame, bounds);
        var state = Profile().States[0] with
        {
            Anchors =
            [
                new LearnedSceneAnchor("壊れたOCR一", [0.1, 0.1, 0.1, 0.03], "e1"),
                new LearnedSceneAnchor("壊れたOCR二", [0.8, 0.1, 0.1, 0.03], "e2"),
            ],
            Affordances =
            [
                new LearnedAffordanceSignature(
                    "affordance:image", "locator:image:v1", "画像", bounds, ["click"], ["e3"],
                    VisualPatch: patch),
            ],
        };
        var profile = Profile() with { States = [state] };

        var scene = LearnedSceneMatcher.Match(
            profile,
            frame,
            new OcrFrameSnapshot("windows-ocr:v1", "ja", []));

        Assert.Equal(StateIdentityStatus.Known, scene.StateIdentity);
        Assert.Equal(state.StateId, scene.StateHypothesisId);
        Assert.Equal("visual", Assert.Single(scene.Affordances).SemanticKind);
    }

    [Fact]
    public void Different_visual_patch_does_not_identify_state_when_ocr_anchors_are_missing()
    {
        var baseline = PixelFrame(20);
        var bounds = new[] { 0.55, 0.55, 0.20, 0.20 };
        var state = Profile().States[0] with
        {
            Affordances =
            [
                new LearnedAffordanceSignature(
                    "affordance:image", "locator:image:v1", "画像", bounds, ["click"], ["e3"],
                    VisualPatch: VisualPatchMatcher.Capture(baseline, bounds)),
            ],
        };
        var profile = Profile() with { States = [state] };

        var scene = LearnedSceneMatcher.Match(
            profile,
            PixelFrame(220),
            new OcrFrameSnapshot("windows-ocr:v1", "ja", []));

        Assert.Equal(StateIdentityStatus.Novel, scene.StateIdentity);
        Assert.Empty(scene.Affordances);
    }

    [Fact]
    public void Cleaner_similar_ocr_replaces_saved_text_without_changing_ids_or_history()
    {
        var original = Profile();
        var state = original.States[0] with
        {
            Anchors =
            [
                new LearnedSceneAnchor("前哨%地", [0.50, 0.89, 0.06, 0.03], "old-anchor"),
                original.States[0].Anchors[1],
            ],
            Affordances =
            [
                original.States[0].Affordances[0] with { Text = "部%隊" },
            ],
        };
        var profile = original with
        {
            States = [state],
            EvidenceIds = original.EvidenceIds.Append("old-anchor").ToArray(),
        };
        var ocr = new OcrFrameSnapshot(
            "windows-ocr:v1", "ja",
            [
                new OcrWordBox("前哨基地", 500, 890, 60, 30),
                new OcrWordBox("隊員募集", 640, 890, 90, 30),
                new OcrWordBox("部隊", 440, 890, 50, 30),
            ]);

        var refined = LearnedSceneMatcher.RefineText(profile, Frame(), ocr);

        var refinedState = Assert.Single(refined.States);
        Assert.Equal(state.StateId, refinedState.StateId);
        Assert.Equal("前哨基地", refinedState.Anchors[0].Text);
        Assert.Contains("前哨%地", refinedState.Anchors[0].PreviousTexts!);
        var action = Assert.Single(refinedState.Affordances);
        Assert.Equal("affordance:squad", action.CandidateId);
        Assert.Equal("部隊", action.Text);
        Assert.Contains("部%隊", action.PreviousTexts!);
        Assert.Contains("old-anchor", refined.EvidenceIds);
        Assert.Contains(refined.EvidenceIds, id => id.StartsWith("ocr-refine:", StringComparison.Ordinal));
    }

    private static LearnedSceneProfileDocument Profile() => new(
        "0.3.0", "profile:1", "profile:v1", "game", "env", "game", "Game", 500, 0.04,
        [new LearnedStateSceneSignature(
            "state:lobby", "signature:v1",
            [
                new LearnedSceneAnchor("ロビー", [0.50, 0.89, 0.06, 0.03], "e1"),
                new LearnedSceneAnchor("隊員募集", [0.64, 0.89, 0.09, 0.03], "e2"),
            ],
            [new LearnedAffordanceSignature(
                "affordance:squad", "locator:squad:v1", "部隊", [0.44, 0.89, 0.05, 0.03], ["click"], ["e3"])],
            ["e1", "e2"])],
        ["profile-evidence"]);

    private static CapturedFrame Frame() => new(
        "0.2.0", "window:game", CaptureBackend.WindowsGraphicsCapture, 1, 1000,
        DateTimeOffset.UnixEpoch, 1000, 1000, "B8G8R8A8_UNorm", 96, 96, 1, 0, 0);

    private static CapturedFrame PixelFrame(byte value)
    {
        const int width = 100;
        const int height = 100;
        var pixels = Enumerable.Repeat(value, width * height * 4).ToArray();
        return new CapturedFrame(
            "0.3.0", "window:game", CaptureBackend.WindowsGraphicsCapture, 1, 1_000,
            DateTimeOffset.UnixEpoch, width, height, "B8G8R8A8_UNorm", 96, 96, 1, 0, 0,
            Pixels: new FramePixels(pixels, width * 4));
    }
}
