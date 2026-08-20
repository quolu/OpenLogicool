using System.Text.Json.Serialization;

namespace OpenLogicool.Contracts.Playbooks;

public enum GameAutomationMode
{
    Observe,
    Assist,
    Auto,
}

public enum GamePolicyReviewStatus
{
    Confirmed,
    Unverified,
    Changed,
    InterpretationUnknown,
}

/// <summary>規約の実解釈ではなく、確認状態と mode ごとの許可を記録する値。</summary>
public sealed record GamePolicyRecord(
    string SchemaVersion,
    string GameId,
    GamePolicyReviewStatus ReviewStatus,
    [property: JsonPropertyName("allowedModes")]
    IReadOnlyList<GameAutomationMode> AllowedModes);
