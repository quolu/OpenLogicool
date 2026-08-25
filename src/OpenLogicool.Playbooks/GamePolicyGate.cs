using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Playbooks;

public enum GamePolicyGateReason
{
    Allowed,
    ModeNotAllowed,
}

public sealed record GamePolicyDecision(bool IsAllowed, GamePolicyGateReason Reason);

/// <summary>利用者がGame Policyへ明示したmodeだけからautomation可否を決めるpure gate。review statusは表示情報。</summary>
public static class GamePolicyGate
{
    public static GamePolicyDecision Evaluate(GamePolicyRecord record, GameAutomationMode requestedMode)
    {
        ArgumentNullException.ThrowIfNull(record);
        Validate(record);

        return record.AllowedModes.Contains(requestedMode)
            ? new GamePolicyDecision(true, GamePolicyGateReason.Allowed)
            : new GamePolicyDecision(false, GamePolicyGateReason.ModeNotAllowed);
    }

    private static void Validate(GamePolicyRecord record)
    {
        var isRevision01 = string.Equals(record.SchemaVersion, ContractSchemaVersions.Revision01, StringComparison.Ordinal);
        var isRevision02 = string.Equals(record.SchemaVersion, ContractSchemaVersions.Revision02, StringComparison.Ordinal);
        if (!isRevision01 && !isRevision02)
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

        if (isRevision01 && record.AllowedModes.Contains(GameAutomationMode.Explore))
        {
            throw new ArgumentException("GamePolicyRecord 0.1.0 は Explore mode を表現できません。", nameof(record));
        }
    }

}
