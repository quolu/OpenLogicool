using OpenLogicool.Contracts.Perception;
using Xunit;

namespace OpenLogicool.Perception.Tests;

public sealed class OcrTextMatcherTests
{
    [Theory]
    [InlineData("アーク", "ァーク")]
    [InlineData("前哨基地", "前哨地")]
    public void Light_ocr_distance_accepts_small_recognition_drift(string expected, string observed)
    {
        Assert.True(OcrTextMatcher.IsSimilar(expected, observed));
    }

    [Fact]
    public void Unrelated_words_are_not_matched()
    {
        Assert.False(OcrTextMatcher.IsSimilar("フレンド", "作戦へ出撃"));
    }

    [Fact]
    public void Cleaner_similar_observation_replaces_noisy_saved_text()
    {
        Assert.True(OcrTextMatcher.PreferObserved("前哨%地", "前哨基地"));
        Assert.False(OcrTextMatcher.PreferObserved("前哨基地", "前哨%地"));
    }
}
