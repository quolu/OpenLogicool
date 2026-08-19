using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Domain;
using Xunit;

namespace OpenLogicool.Domain.Tests;

public sealed class AttemptFaultClassifierTests
{
    [Theory]
    [InlineData(AttemptFaultPoint.HandledStop)]
    [InlineData(AttemptFaultPoint.TargetWindowLost)]
    public void Guaranteed_uncalled_faults_disarm(AttemptFaultPoint faultPoint)
    {
        // §6.7: 外部入力 API を一度も呼んでいないことを保証できる場合だけが Disarmed。
        Assert.Equal(
            AttemptState.Disarmed,
            AttemptFaultClassifier.Classify(faultPoint, ExternalInputCallState.ProvablyNotCalled));
    }

    [Theory]
    [InlineData(AttemptFaultPoint.HandledStop)]
    [InlineData(AttemptFaultPoint.TargetWindowLost)]
    [InlineData(AttemptFaultPoint.PartialSendInput)]
    public void Unguaranteed_faults_always_become_outcome_unknown(AttemptFaultPoint faultPoint)
    {
        Assert.Equal(
            AttemptState.OutcomeUnknown,
            AttemptFaultClassifier.Classify(faultPoint, ExternalInputCallState.CalledOrUnknown));
    }

    [Fact]
    public void A_partial_send_input_contradicts_the_uncalled_guarantee()
    {
        // 矛盾した保証主張を黙って安全側へ丸めない——保証の出所の誤りを隠さない。
        Assert.Throws<ArgumentException>(() => AttemptFaultClassifier.Classify(
            AttemptFaultPoint.PartialSendInput, ExternalInputCallState.ProvablyNotCalled));
    }
}
