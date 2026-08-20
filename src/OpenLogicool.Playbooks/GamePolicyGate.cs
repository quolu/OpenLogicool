using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Playbooks;

public enum GamePolicyGateReason
{
    Allowed,
    Unverified,
    Changed,
    InterpretationUnknown,
    ModeNotAllowed,
}

public sealed record GamePolicyDecision(bool IsAllowed, GamePolicyGateReason Reason);

/// <summary>規約確認状態と mode 許可だけから automation 可否を決める pure gate。</summary>
public static class GamePolicyGate
{
    public static GamePolicyDecision Evaluate(GamePolicyRecord record, GameAutomationMode requestedMode)
    {
        ArgumentNullException.ThrowIfNull(record);
        Validate(record);

        if (requestedMode is GameAutomationMode.Assist or GameAutomationMode.Auto
            && record.ReviewStatus is not GamePolicyReviewStatus.Confirmed)
        {
            return new GamePolicyDecision(false, ReviewReason(record.ReviewStatus));
        }

        return record.AllowedModes.Contains(requestedMode)
            ? new GamePolicyDecision(true, GamePolicyGateReason.Allowed)
            : new GamePolicyDecision(false, GamePolicyGateReason.ModeNotAllowed);
    }

    private static void Validate(GamePolicyRecord record)
    {
        if (!string.Equals(record.SchemaVersion, ContractSchemaVersions.Revision01, StringComparison.Ordinal))
        {
            throw new ArgumentException("未対応の GamePolicyRecord schema version です。", nameof(record));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(record.GameId);
        ArgumentNullException.ThrowIfNull(record.AllowedModes);
        if (!Enum.IsDefined(record.ReviewStatus)
            || record.AllowedModes.Any(mode => !Enum.IsDefined(mode))
            || record.AllowedModes.Distinct().Count() != record.AllowedModes.Count)
        {
            throw new ArgumentException("GamePolicyRecord の mode または review status が不正です。", nameof(record));
        }
    }

    private static GamePolicyGateReason ReviewReason(GamePolicyReviewStatus status) => status switch
    {
        GamePolicyReviewStatus.Unverified => GamePolicyGateReason.Unverified,
        GamePolicyReviewStatus.Changed => GamePolicyGateReason.Changed,
        GamePolicyReviewStatus.InterpretationUnknown => GamePolicyGateReason.InterpretationUnknown,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}
