using OpenLogicool.Probe;
using OpenLogicool.AI;
using Xunit;

namespace OpenLogicool.Probe.Tests;

public sealed class LiveDiscoveryObserveSmokeTests
{
    [Fact]
    public void Title_screen_label_is_unique_when_windows_ocr_splits_ascii_o_as_katakana_noise()
    {
        var snapshot = new WindowsOcrSnapshot(
            "TロUCHTOCONTINUE",
            1,
            "ja-JP",
            10_000,
            ["TロUCHTOCONTINUE"],
            [
                new WindowsOcrWord("T", 1237.5, 984.5, 13, 19),
                new WindowsOcrWord("ロ", 1252.5, 984.5, 13, 19),
                new WindowsOcrWord("UCH", 1268.5, 984.5, 46.5, 19),
                new WindowsOcrWord("TO", 1324.5, 984.5, 28.5, 19),
                new WindowsOcrWord("CONTINUE", 1362.5, 984.5, 118.5, 19),
                new WindowsOcrWord("1", 1492, 990, 4.5, 12),
            ]);

        var result = LiveDiscoveryObserveSmoke.Ground("TOUCH TO CONTINUE", snapshot);

        Assert.Equal(14d / 15d, FrameBoundLabelMatcher.Similarity("TロUCHTOCONTINUE", "TOUCH TO CONTINUE"));
        Assert.Equal("Grounded", result.Status);
        Assert.Equal("FuzzyUnique", result.MatchKind);
        Assert.Equal("TロUCHTOCONTINUE", result.Box?.Text);
    }

    [Fact]
    public void Same_label_at_two_separate_positions_remains_unknown()
    {
        var snapshot = new WindowsOcrSnapshot(
            "TOUCH TO CONTINUE\nTOUCH TO CONTINUE",
            1,
            "en-US",
            10_000,
            ["TOUCH TO CONTINUE", "TOUCH TO CONTINUE"],
            [
                new WindowsOcrWord("TOUCH", 100, 100, 80, 20),
                new WindowsOcrWord("TO", 190, 100, 30, 20),
                new WindowsOcrWord("CONTINUE", 230, 100, 120, 20),
                new WindowsOcrWord("TOUCH", 100, 300, 80, 20),
                new WindowsOcrWord("TO", 190, 300, 30, 20),
                new WindowsOcrWord("CONTINUE", 230, 300, 120, 20),
            ]);

        var result = LiveDiscoveryObserveSmoke.Ground("TOUCH TO CONTINUE", snapshot);

        Assert.Equal("Unknown", result.Status);
        Assert.Equal(2, result.MatchCount);
    }

    [Fact]
    public void Diagonal_single_character_ocr_tokens_reconstruct_one_japanese_question()
    {
        var snapshot = new WindowsOcrSnapshot(
            "ゲームを終了しますか?",
            1,
            "ja-JP",
            10_000,
            ["ゲームを終了しますか?"],
            [
                new WindowsOcrWord("ゲ", 1257.5, 554.5, 16, 17.5),
                new WindowsOcrWord("ー", 1275.5, 559.5, 16, 4),
                new WindowsOcrWord("ム", 1294, 552, 16, 15.5),
                new WindowsOcrWord("を", 1312, 549.5, 15, 16.5),
                new WindowsOcrWord("終", 1328.5, 547.5, 17, 17.5),
                new WindowsOcrWord("了", 1346, 546, 15.5, 17.5),
                new WindowsOcrWord("し", 1367, 544.5, 13.5, 16),
                new WindowsOcrWord("ま", 1383.5, 542, 15, 17),
                new WindowsOcrWord("す", 1400, 540, 16.5, 17.5),
                new WindowsOcrWord("か", 1418.5, 538.5, 16.5, 16.5),
                new WindowsOcrWord("?", 1439, 536.5, 10.5, 15.5),
            ]);

        var result = LiveDiscoveryObserveSmoke.Ground("ゲームを終了しますか?", snapshot);

        Assert.Equal("Grounded", result.Status);
        Assert.Equal("ExactUnique", result.MatchKind);
    }
}
