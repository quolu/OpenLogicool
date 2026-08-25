using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class WindowsGameOcrSpanBuilderTests
{
    [Fact]
    public void Adjacent_japanese_characters_form_spans_and_small_kana_variant()
    {
        var ocr = new WindowsGameOcrResult(
            "ァ ー ク",
            "ja-JP",
            10,
            [
                new WindowsGameOcrWord("ァ", 100, 200, 12, 20),
                new WindowsGameOcrWord("ー", 114, 200, 12, 20),
                new WindowsGameOcrWord("ク", 128, 200, 12, 20),
            ]);

        var regions = WindowsGameOcrSpanBuilder.Build(ocr, 1920, 1080);

        Assert.Contains(regions, region => region.Text == "ァーク");
        var normalized = Assert.Single(regions, region => region.Text == "アーク");
        Assert.Equal(100d / 1920, normalized.EvidenceRegion.NormalizedBounds[0]);
        Assert.Equal(40d / 1920, normalized.EvidenceRegion.NormalizedBounds[2]);

        var canonical = WindowsGameOcrSpanBuilder.Canonicalize(regions);
        Assert.Contains(canonical, region => region.Text == "アーク");
        Assert.DoesNotContain(canonical, region => region.Text == "アー");
    }

    [Fact]
    public void Large_horizontal_gap_keeps_menu_items_separate()
    {
        var ocr = new WindowsGameOcrResult(
            "部 隊 ショップ",
            "ja-JP",
            10,
            [
                new WindowsGameOcrWord("部", 100, 200, 12, 20),
                new WindowsGameOcrWord("隊", 114, 200, 12, 20),
                new WindowsGameOcrWord("ショップ", 800, 200, 80, 20),
            ]);

        var regions = WindowsGameOcrSpanBuilder.Build(ocr, 1920, 1080);

        Assert.Contains(regions, region => region.Text == "部隊");
        Assert.DoesNotContain(regions, region => region.Text.Contains("部隊ショップ", StringComparison.Ordinal));

        var canonical = WindowsGameOcrSpanBuilder.Canonicalize(regions);
        Assert.Contains(canonical, region => region.Text == "部隊");
        Assert.Contains(canonical, region => region.Text == "ショップ");
    }
}
