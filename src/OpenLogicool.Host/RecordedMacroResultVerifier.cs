using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Exploration;

namespace OpenLogicool.Host;

public enum RecordedMacroResultStatus { Matched, Different, Unavailable }

public sealed record RecordedMacroResultVerification(
    int StepNumber, RecordedMacroResultStatus Status, string? ExpectedObservationId,
    string? ActualObservationId, string Detail, string? ExpectedImagePath = null,
    IReadOnlyList<string>? ExpectedTexts = null);

public interface IRecordedMacroResultVerifier
{
    RecordedMacroResultVerification Verify(DemonstrationRouteStep step, int stepNumber, ObservedScene? actual);
}

/// <summary>遷移の有無とは別に、保存原本の実観測と再生結果の文字配置・画像特徴を照合する。</summary>
public sealed class RecordedMacroResultVerifier(
    Func<string, DemonstrationSessionRecord?> loadSession,
    Action<RecordedMacroResultVerification>? publish = null) : IRecordedMacroResultVerifier
{
    private readonly Dictionary<string, DemonstrationSessionRecord?> sessions = new(StringComparer.Ordinal);

    public RecordedMacroResultVerification Verify(DemonstrationRouteStep step, int stepNumber, ObservedScene? actual)
    {
        if (!sessions.TryGetValue(step.SessionId, out var session))
            sessions.Add(step.SessionId, session = loadSession(step.SessionId));
        var operation = session?.Events.Select(item => item.Operation)
            .SingleOrDefault(item => item?.OperationId == step.OperationId);
        var expected = operation?.After.Observations
            .SingleOrDefault(scene => scene.ObservationId == operation.Comparison.AfterObservationId)
            ?? (operation?.After.StableScene?.ObservationId == operation?.Comparison.AfterObservationId
                ? operation?.After.StableScene : null);
        var status = expected is null || actual is null
            || expected.CaptureAvailability != CaptureAvailability.Available
            || actual.CaptureAvailability != CaptureAvailability.Available
            || !HasEvidence(expected) || !HasEvidence(actual)
                ? RecordedMacroResultStatus.Unavailable
                : MatchesLocalLayout(expected, actual)
                    ? RecordedMacroResultStatus.Matched : RecordedMacroResultStatus.Different;
        var result = new RecordedMacroResultVerification(stepNumber, status,
            expected?.ObservationId, actual?.ObservationId, status switch
            {
                RecordedMacroResultStatus.Matched => $"手順 {stepNumber} は録画した結果と一致しました。",
                RecordedMacroResultStatus.Different => $"手順 {stepNumber} は録画と違う画面になりました。後続の手順へ進みません。",
                _ => $"手順 {stepNumber} の録画と再生結果を比較する根拠を取得できませんでした。",
            }, expected?.Frame.Artifact?.LocalPath, expected?.DiscoveryEvidence?.LocalGroundingTexts);
        publish?.Invoke(result);
        return result;
    }

    // 別sessionのStateIdや、共通の「戻る」だけで付いた既知stateを再現成功の根拠にしない。
    public static bool MatchesLocalLayout(ObservedScene expected, ObservedScene actual) =>
        GameSceneSemanticComparer.StableEquivalent(Local(expected), Local(actual));

    private static ObservedScene Local(ObservedScene scene) => scene with
    { StateIdentity = StateIdentityStatus.Novel, StateHypothesisId = null, StateCandidates = [] };

    private static bool HasEvidence(ObservedScene scene) =>
        GameSceneSemanticComparer.Signature(Local(scene)).HasEvidence || scene.SceneVisualPatch is not null;
}
