using OpenLogicool.AI;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class VisualControlLocalGrounderTests
{
    [Fact]
    public void Image_button_keeps_provider_coordinates_without_rebinding_to_distant_ocr()
    {
        var candidate = Candidate("icon", "アーク", [0.62, 0.68, 0.18, 0.14]);
        var exact = Region("アーク", [0.62, 0.47, 0.08, 0.04]);

        var grounded = VisualControlLocalGrounder.Ground(
            candidate,
            [exact, Region("作戦へ出撃", [0.62, 0.68, 0.18, 0.08])],
            Frame());

        Assert.NotNull(grounded);
        Assert.Equal(candidate.Locator.NormalizedBounds, grounded.Locator.NormalizedBounds);
        Assert.DoesNotContain(exact.EvidenceRegion, grounded.EvidenceRegions);
        Assert.NotNull(grounded.VisualPatch);
    }

    [Fact]
    public void Semantic_label_without_exact_ocr_is_kept_until_transition_result()
    {
        var grounded = VisualControlLocalGrounder.Ground(
            Candidate("icon", "メール", [0.03, 0.15, 0.07, 0.07]),
            [Region("Messenger", [0.02, 0.16, 0.08, 0.03])],
            Frame());

        Assert.NotNull(grounded);
        Assert.NotNull(grounded.VisualPatch);
    }

    [Fact]
    public void Icon_only_control_without_ocr_keeps_provider_bounds_and_records_visual_patch()
    {
        var bounds = new[] { 0.90, 0.05, 0.05, 0.05 };

        var grounded = VisualControlLocalGrounder.Ground(
            Candidate("icon", "通知", bounds),
            [Region("ロビー", [0.40, 0.80, 0.10, 0.04])],
            Frame());

        Assert.NotNull(grounded);
        Assert.Equal(bounds, grounded.Locator.NormalizedBounds);
        Assert.NotNull(grounded.VisualPatch);
    }

    [Fact]
    public void Text_control_without_exact_same_frame_ocr_is_kept_until_transition_result()
    {
        var grounded = VisualControlLocalGrounder.Ground(
            Candidate("text", "アーク", [0.62, 0.47, 0.15, 0.10]),
            [],
            Frame());

        Assert.NotNull(grounded);
        Assert.NotNull(grounded.VisualPatch);
    }

    private static AffordanceCandidate Candidate(
        string kind,
        string label,
        IReadOnlyList<double> bounds) => new(
        ContractSchemaVersions.Revision03,
        "candidate:1",
        "observation:1",
        1,
        1,
        "window:game",
        new AffordanceLocator(ContractSchemaVersions.Revision03, "foundry-local-region", bounds, "locator:1"),
        [new EvidenceRegion(ContractSchemaVersions.Revision03, "rect", bounds, "foundry-local")],
        0.5,
        ["click"],
        kind,
        label);

    private static LocalVisionTextRegion Region(string text, IReadOnlyList<double> bounds) => new(
        text,
        new EvidenceRegion(ContractSchemaVersions.Revision03, "rect", bounds, "windows-ocr"));

    private static CapturedFrame Frame()
    {
        const int width = 100;
        const int height = 100;
        return new CapturedFrame(
            ContractSchemaVersions.Revision03,
            "window:game",
            CaptureBackend.WindowsGraphicsCapture,
            1,
            1,
            DateTimeOffset.UnixEpoch,
            width,
            height,
            "BGRA8",
            96,
            96,
            1,
            0,
            0,
            Pixels: new FramePixels(new byte[width * height * 4], width * 4));
    }
}
