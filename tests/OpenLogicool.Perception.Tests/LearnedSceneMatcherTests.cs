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
}
