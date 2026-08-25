using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Playbooks;

public enum GameOperatorCapability
{
    ObserveOnly,
    Teach,
    Supervised,
    Verified,
}

public enum CapabilityReleaseGateReason
{
    Released,
    DisabledByRelease,
    GamePolicyDenied,
}

/// <summary>配布 release ごとの capability 明示設定。</summary>
public sealed record CapabilityReleaseSettings(
    bool ObserveOnlyEnabled,
    bool TeachEnabled,
    bool SupervisedEnabled,
    bool VerifiedEnabled)
{
    public bool IsEnabled(GameOperatorCapability capability) => capability switch
    {
        GameOperatorCapability.ObserveOnly => ObserveOnlyEnabled,
        GameOperatorCapability.Teach => TeachEnabled,
        GameOperatorCapability.Supervised => SupervisedEnabled,
        GameOperatorCapability.Verified => VerifiedEnabled,
        _ => throw new ArgumentOutOfRangeException(nameof(capability)),
    };
}

public sealed record CapabilityReleaseDecision(bool IsReleased, CapabilityReleaseGateReason Reason);

/// <summary>
/// capability の公開可否をrelease設定と利用者が明示したGame Policy modeへ束縛する。
/// ObserveOnly／TeachSupervised の proposal 処理や GamePolicyGate の規約解釈を再実装しない。
/// </summary>
public static class CapabilityRelease
{
    public static CapabilityReleaseDecision Evaluate(
        CapabilityReleaseSettings settings,
        GameOperatorCapability capability,
        GamePolicyRecord policy)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(policy);

        if (!settings.IsEnabled(capability))
        {
            return new CapabilityReleaseDecision(false, CapabilityReleaseGateReason.DisabledByRelease);
        }

        if (!GamePolicyGate.Evaluate(policy, RequiredPolicyMode(capability)).IsAllowed)
        {
            return new CapabilityReleaseDecision(false, CapabilityReleaseGateReason.GamePolicyDenied);
        }

        return new CapabilityReleaseDecision(true, CapabilityReleaseGateReason.Released);
    }

    private static GameAutomationMode RequiredPolicyMode(GameOperatorCapability capability) => capability switch
    {
        GameOperatorCapability.ObserveOnly => GameAutomationMode.Observe,
        GameOperatorCapability.Teach => GameAutomationMode.Assist,
        GameOperatorCapability.Supervised => GameAutomationMode.Assist,
        GameOperatorCapability.Verified => GameAutomationMode.Auto,
        _ => throw new ArgumentOutOfRangeException(nameof(capability)),
    };
}
