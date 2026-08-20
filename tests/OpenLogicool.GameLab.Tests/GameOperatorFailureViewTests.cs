using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.GameLab;
using Xunit;

namespace OpenLogicool.GameLab.Tests;

public sealed class GameOperatorFailureViewTests
{
    [Fact]
    public void Capture_fault_is_explicit_and_never_offers_hidden_fallback()
    {
        var messages = GameOperatorFailureView.Project(new(new CaptureFault(CaptureFaultKind.Occluded, "window hidden"), ObservationStatus.Known, false, VerificationLevel.Unverified));
        Assert.Contains(messages, message => message.Title == "画面取得を停止しました" && message.NextAction.Contains("自動で切り替えません", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.Title == "対応状況は未確認です");
    }

    [Theory]
    [InlineData(ObservationStatus.Ambiguous)]
    [InlineData(ObservationStatus.Unknown)]
    [InlineData(ObservationStatus.Unavailable)]
    public void Non_known_observation_is_visible_and_not_an_automatic_condition(ObservationStatus status)
    {
        var messages = GameOperatorFailureView.Project(new(null, status, false, VerificationLevel.Confirmed));
        Assert.Contains(messages, message => message.Title == "画面状態を確定できません");
    }

    [Fact]
    public void Absolute_coordinate_only_step_is_marked_fragile()
    {
        var messages = GameOperatorFailureView.Project(new(null, ObservationStatus.Known, true, VerificationLevel.StrongInference));
        Assert.Contains(messages, message => message.Title == "この操作は画面配置に依存します");
    }
}
