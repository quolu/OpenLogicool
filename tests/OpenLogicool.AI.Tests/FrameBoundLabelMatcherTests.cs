using Xunit;

namespace OpenLogicool.AI.Tests;

public sealed class FrameBoundLabelMatcherTests
{
    [Fact]
    public void 日本語ラベルを捨てずに一致できる()
    {
        Assert.True(FrameBoundLabelMatcher.Equals("イベント", "イベント"));
        Assert.True(FrameBoundLabelMatcher.Contains("メインメニュー イベント", "イベント"));
    }

    [Fact]
    public void 期待ラベルにない文字体系のOCRノイズを除く()
    {
        Assert.True(FrameBoundLabelMatcher.Equals("state.mainロ-menu", "state.main-menu"));
        Assert.False(FrameBoundLabelMatcher.Equals("state.main-menu.event-popup", "state.main-menu"));
        Assert.True(FrameBoundLabelMatcher.Contains("イベントOpen", "イベント"));
        Assert.True(FrameBoundLabelMatcher.Contains("イベントOpen", "Open"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void 正規化後に空になる期待値は一致しない(string expected)
    {
        Assert.False(FrameBoundLabelMatcher.Equals("OpenEvent", expected));
        Assert.False(FrameBoundLabelMatcher.Contains("OpenEvent", expected));
    }

    [Fact]
    public void 空白を除いて英字を大小無視し保持対象のハイフンは区別する()
    {
        Assert.True(FrameBoundLabelMatcher.Equals("Open Event", "open event"));
        Assert.False(FrameBoundLabelMatcher.Equals("Open Event", "open-event"));
        Assert.False(FrameBoundLabelMatcher.Equals("EventShop", "Event"));
    }

    [Fact]
    public void OCRの少数誤読を正規化Levenshtein類似度で測る()
    {
        Assert.Equal(1, FrameBoundLabelMatcher.Similarity("TOUCH TO CONTINUE", "touch to continue"));
        Assert.True(FrameBoundLabelMatcher.Similarity("TOIJCHTOCONTINUE", "TOUCH TO CONTINUE") >= 0.85);
        Assert.True(FrameBoundLabelMatcher.Similarity("ログアウト", "TOUCH TO CONTINUE") < 0.5);
        Assert.Equal(0, FrameBoundLabelMatcher.Similarity("OpenEvent", "!!!"));
    }
}
