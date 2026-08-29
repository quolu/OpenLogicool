using System.Security.Cryptography;
using System.Text;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Contracts.Playbooks;

/// <summary>デモ操作を発生させた物理入力源。</summary>
public enum DemonstrationInputSource
{
    Mouse,
    Keyboard,
    G13,
    G600,
}

/// <summary>操作デモ原本へ追記できるeventの種別。</summary>
public enum DemonstrationEventKind
{
    Operation,
    FocusLost,
    FocusRegained,
    Stopped,
}

/// <summary>原本が追記を受け付けるかどうか。</summary>
public enum DemonstrationSessionState
{
    Recording,
    Stopped,
}

/// <summary>
/// 記録開始時に一度だけ書く、操作デモ原本の見出し。
/// 目的、対象game、対象app、対象windowを固定し、以後書き換えない。
/// </summary>
public sealed record DemonstrationSessionDraft(
    string SchemaVersion,
    string SessionId,
    string GameId,
    string EnvironmentScope,
    string Goal,
    string TargetApplicationPath,
    string TargetWindowSourceId,
    string RecorderVersion,
    DateTimeOffset StartedUtc);

/// <summary>
/// デモ操作を操作時のObservationとclient frameへ束縛する。
/// desktop絶対座標を保持するfieldは持たず、正規化座標だけを保存する。
/// </summary>
public sealed record DemonstrationFrameBinding(
    string SchemaVersion,
    string ObservationId,
    long FrameSequence,
    long TransformRevision,
    string TargetWindowSourceId,
    IReadOnlyList<double>? NormalizedPoint);

/// <summary>
/// 利用者の有限操作一回と、その操作前後の観測・10秒Compare・evidenceの束。
/// </summary>
public sealed record DemonstrationOperation(
    string SchemaVersion,
    string OperationId,
    string Operation,
    DemonstrationInputSource Source,
    DemonstrationFrameBinding Target,
    ObservedScene Before,
    GameInteractionStabilityResult After,
    GameTransitionComparison Comparison,
    string TransitionEvidenceId,
    long OperationMonotonicMilliseconds,
    long ObservationCompletedMonotonicMilliseconds,
    DateTimeOffset OccurredUtc,
    IReadOnlyList<string>? KeyTokens = null,
    string? DeviceControlId = null,
    int? VerticalScrollSteps = null,
    int? HorizontalScrollSteps = null,
    IReadOnlyList<double>? DragDestinationNormalized = null);

/// <summary>
/// 対象gameがforegroundから外れた区間の境界。
/// 他appの画面・座標・key文字列を持つfieldは無く、実行fileのpathだけを残す。
/// pathがnullなのはforeground identityを取得できなかった区間で、
/// 「識別できなかった」ことをそのまま残す（別の値で埋めない）。
/// </summary>
public sealed record DemonstrationFocusChange(
    string SchemaVersion,
    string? ForegroundApplicationPath,
    string? ResumedObservationId,
    DateTimeOffset OccurredUtc);

/// <summary>記録停止。これ以降この原本は追記を受け付けない。</summary>
public sealed record DemonstrationStop(
    string SchemaVersion,
    string Reason,
    DateTimeOffset OccurredUtc);

public sealed record DemonstrationEventDraft(
    string SchemaVersion,
    string SessionId,
    DemonstrationEventKind Kind,
    DateTimeOffset OccurredUtc,
    DemonstrationOperation? Operation = null,
    DemonstrationFocusChange? FocusChange = null,
    DemonstrationStop? Stop = null);

public sealed record DemonstrationEvent(
    string SchemaVersion,
    string SessionId,
    long Sequence,
    string EventId,
    string? ParentRevisionId,
    string ResultingRevisionId,
    DemonstrationEventKind Kind,
    DateTimeOffset OccurredUtc,
    DateTimeOffset PersistedUtc,
    DemonstrationOperation? Operation,
    DemonstrationFocusChange? FocusChange,
    DemonstrationStop? Stop);

/// <summary>原本の現在の全量。EventsはSequence昇順で、途中を欠かさない。</summary>
public sealed record DemonstrationSessionRecord(
    DemonstrationSessionDraft Session,
    DemonstrationSessionState State,
    string? RevisionId,
    IReadOnlyList<DemonstrationEvent> Events);

public static class DemonstrationRevisionIds
{
    public static string EventId(string sessionId, long sequence, string payloadJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(payloadJson);
        return $"demo-event:{Digest($"{sessionId}\n{sequence}\n{payloadJson}")}";
    }

    public static string Next(string sessionId, string? parentRevisionId, long sequence, string eventId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        return $"demo:{Digest($"{sessionId}\n{parentRevisionId ?? "root"}\n{sequence}\n{eventId}")}";
    }

    private static string Digest(string material) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
}

/// <summary>
/// 操作デモ原本のappend-only store。更新・削除のmethodを持たない。
/// </summary>
public interface IDemonstrationSessionStore
{
    DemonstrationSessionRecord Start(DemonstrationSessionDraft draft);

    DemonstrationEvent Append(DemonstrationEventDraft draft);

    DemonstrationSessionRecord? Load(string sessionId);

    IReadOnlyList<string> ListSessionIds(string gameId, string environmentScope);
}

/// <summary>
/// 操作デモ原本の受入規則。storeとGame Operatorが同じ規則を通るよう、
/// 規則をここ一箇所だけに置く（Persistenceはこのcontractへ依存し、Playbooksへは依存しない）。
/// </summary>
public static class DemonstrationSessionValidator
{
    public static void ValidateSession(DemonstrationSessionDraft session)
    {
        ArgumentNullException.ThrowIfNull(session);
        RequireSchema(session.SchemaVersion, nameof(session));
        Require(!string.IsNullOrWhiteSpace(session.SessionId), "SessionIdが空です。");
        Require(!string.IsNullOrWhiteSpace(session.GameId), "GameIdが空です。");
        Require(!string.IsNullOrWhiteSpace(session.EnvironmentScope), "EnvironmentScopeが空です。");
        Require(!string.IsNullOrWhiteSpace(session.Goal), "Goalが空です。");
        Require(!string.IsNullOrWhiteSpace(session.TargetApplicationPath), "TargetApplicationPathが空です。");
        Require(!string.IsNullOrWhiteSpace(session.TargetWindowSourceId), "TargetWindowSourceIdが空です。");
        Require(!string.IsNullOrWhiteSpace(session.RecorderVersion), "RecorderVersionが空です。");
        Require(session.StartedUtc != default, "StartedUtcが未設定です。");
    }

    /// <summary>既存eventの並びに対して、この追記が受理できるかを判定する。</summary>
    public static void ValidateAppend(
        DemonstrationSessionDraft session,
        IReadOnlyList<DemonstrationEvent> existingEvents,
        DemonstrationEventDraft draft)
    {
        ValidateSession(session);
        ArgumentNullException.ThrowIfNull(existingEvents);
        ArgumentNullException.ThrowIfNull(draft);
        RequireSchema(draft.SchemaVersion, nameof(draft));
        Require(
            string.Equals(draft.SessionId, session.SessionId, StringComparison.Ordinal),
            "eventのSessionIdが原本と一致しません。");
        Require(Enum.IsDefined(draft.Kind), "未知のDemonstrationEventKindです。");
        RequireExactPayload(draft);

        Require(
            draft.OccurredUtc >= session.StartedUtc,
            "OccurredUtcが記録開始より前です。");
        if (existingEvents.Count > 0)
        {
            var last = existingEvents[^1];
            Require(draft.OccurredUtc >= last.OccurredUtc, "OccurredUtcが直前のeventより前です。");
            Require(last.Kind != DemonstrationEventKind.Stopped, "停止済みの操作デモ原本へは追記できません。");
        }

        var paused = IsPaused(existingEvents);
        switch (draft.Kind)
        {
            case DemonstrationEventKind.Operation:
                Require(!paused, "focus喪失中の操作は記録しません。");
                ValidateOperation(session, draft.Operation!);
                break;

            case DemonstrationEventKind.FocusLost:
                Require(!paused, "既にfocus喪失中です。");
                RequireSchema(draft.FocusChange!.SchemaVersion, nameof(draft));
                // pathはnull可（foreground identity取得不能）。空白文字だけの値は識別できたことにしない。
                Require(
                    draft.FocusChange.ForegroundApplicationPath is null
                    || draft.FocusChange.ForegroundApplicationPath.Trim().Length > 0,
                    "ForegroundApplicationPathが空白です。識別不能はnullで残します。");
                Require(
                    !string.Equals(
                        draft.FocusChange.ForegroundApplicationPath,
                        session.TargetApplicationPath,
                        StringComparison.OrdinalIgnoreCase),
                    "対象app自身へのfocus喪失は記録できません。");
                Require(
                    draft.FocusChange.ResumedObservationId is null,
                    "focus喪失eventにResumedObservationIdは持たせません。");
                break;

            case DemonstrationEventKind.FocusRegained:
                Require(paused, "focus喪失していない区間ではfocus復帰を記録できません。");
                RequireSchema(draft.FocusChange!.SchemaVersion, nameof(draft));
                Require(
                    string.Equals(
                        draft.FocusChange.ForegroundApplicationPath,
                        session.TargetApplicationPath,
                        StringComparison.OrdinalIgnoreCase),
                    "focus復帰先が対象appではありません。");
                Require(
                    !string.IsNullOrWhiteSpace(draft.FocusChange.ResumedObservationId),
                    "focus復帰は新しいObservationから再開します。");
                break;

            case DemonstrationEventKind.Stopped:
                RequireSchema(draft.Stop!.SchemaVersion, nameof(draft));
                Require(!string.IsNullOrWhiteSpace(draft.Stop.Reason), "停止理由が空です。");
                break;

            default:
                throw new ArgumentException("未知のDemonstrationEventKindです。", nameof(draft));
        }
    }

    public static bool IsPaused(IReadOnlyList<DemonstrationEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        for (var index = events.Count - 1; index >= 0; index--)
        {
            if (events[index].Kind == DemonstrationEventKind.FocusLost)
            {
                return true;
            }

            if (events[index].Kind == DemonstrationEventKind.FocusRegained)
            {
                return false;
            }
        }

        return false;
    }

    private static void ValidateOperation(DemonstrationSessionDraft session, DemonstrationOperation operation)
    {
        RequireSchema(operation.SchemaVersion, nameof(operation));
        Require(!string.IsNullOrWhiteSpace(operation.OperationId), "OperationIdが空です。");
        Require(
            GameInteractionOperations.InputOperations.Contains(operation.Operation, StringComparer.Ordinal),
            $"'{operation.Operation}' は基本入力機能ではありません。");
        Require(Enum.IsDefined(operation.Source), "未知のDemonstrationInputSourceです。");
        Require(!string.IsNullOrWhiteSpace(operation.TransitionEvidenceId), "TransitionEvidenceIdが空です。");
        Require(
            operation.OperationMonotonicMilliseconds >= 0
            && operation.ObservationCompletedMonotonicMilliseconds >= operation.OperationMonotonicMilliseconds,
            "観測完了が操作より前になっています。");

        ValidateBinding(session, operation);
        ValidateSourceAndPrimitive(operation);
        ValidatePrimitiveParameters(operation);
        ValidateBeforeAfter(operation);
    }

    private static void ValidateBinding(DemonstrationSessionDraft session, DemonstrationOperation operation)
    {
        var target = operation.Target;
        ArgumentNullException.ThrowIfNull(target);
        RequireSchema(target.SchemaVersion, nameof(operation));
        Require(!string.IsNullOrWhiteSpace(target.ObservationId), "TargetのObservationIdが空です。");
        Require(
            string.Equals(target.TargetWindowSourceId, session.TargetWindowSourceId, StringComparison.Ordinal),
            "操作対象windowが原本の対象windowと一致しません。");

        if (string.Equals(operation.Operation, GameInteractionOperations.KeyTap, StringComparison.Ordinal))
        {
            Require(target.NormalizedPoint is null, "key操作にpointer座標は保存しません。");
            return;
        }

        Require(target.NormalizedPoint is not null, "pointer操作には正規化座標が必要です。");
        RequireNormalizedPoint(target.NormalizedPoint!, "NormalizedPoint");
    }

    private static void ValidateSourceAndPrimitive(DemonstrationOperation operation)
    {
        var isKeyTap = string.Equals(operation.Operation, GameInteractionOperations.KeyTap, StringComparison.Ordinal);
        switch (operation.Source)
        {
            // G13はpointerを持たず、keyboardはpointer座標を生まない。
            case DemonstrationInputSource.Keyboard:
            case DemonstrationInputSource.G13:
                Require(isKeyTap, "この入力源はkey操作だけを記録します。");
                break;
            case DemonstrationInputSource.Mouse:
                Require(!isKeyTap, "mouse入力源でkey操作は記録しません。");
                break;
            case DemonstrationInputSource.G600:
                break;
            default:
                throw new ArgumentException("未知のDemonstrationInputSourceです。", nameof(operation));
        }

        var isDeviceControl = operation.Source is DemonstrationInputSource.G13 or DemonstrationInputSource.G600;
        Require(
            isDeviceControl == !string.IsNullOrWhiteSpace(operation.DeviceControlId),
            "DeviceControlIdはG13／G600の操作にだけ付けます。");
    }

    private static void ValidatePrimitiveParameters(DemonstrationOperation operation)
    {
        var hasScroll = operation.VerticalScrollSteps is not null || operation.HorizontalScrollSteps is not null;

        switch (operation.Operation)
        {
            case GameInteractionOperations.KeyTap:
                Require(
                    operation.KeyTokens is { Count: > 0 } && !operation.KeyTokens.Any(string.IsNullOrWhiteSpace),
                    "key操作にはKeyTokensが必要です。");
                Require(!hasScroll, "key操作にscroll段数は付けません。");
                Require(operation.DragDestinationNormalized is null, "key操作にdrag先は付けません。");
                break;

            case GameInteractionOperations.Scroll:
                Require(
                    (operation.VerticalScrollSteps ?? 0) != 0 || (operation.HorizontalScrollSteps ?? 0) != 0,
                    "scroll操作には0でない段数が必要です。");
                Require(operation.KeyTokens is null, "scroll操作にKeyTokensは付けません。");
                Require(operation.DragDestinationNormalized is null, "scroll操作にdrag先は付けません。");
                break;

            case GameInteractionOperations.Drag:
                Require(operation.DragDestinationNormalized is not null, "drag操作にはdrag先が必要です。");
                RequireNormalizedPoint(operation.DragDestinationNormalized!, "DragDestinationNormalized");
                Require(operation.KeyTokens is null, "drag操作にKeyTokensは付けません。");
                Require(!hasScroll, "drag操作にscroll段数は付けません。");
                break;

            default:
                Require(operation.KeyTokens is null, "この操作にKeyTokensは付けません。");
                Require(!hasScroll, "この操作にscroll段数は付けません。");
                Require(operation.DragDestinationNormalized is null, "この操作にdrag先は付けません。");
                break;
        }
    }

    private static void ValidateBeforeAfter(DemonstrationOperation operation)
    {
        var before = operation.Before;
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(before.Frame);
        Require(
            string.Equals(before.ObservationId, operation.Target.ObservationId, StringComparison.Ordinal),
            "Beforeが操作束縛のObservationと一致しません。");
        Require(
            before.Frame.Sequence == operation.Target.FrameSequence
            && before.Frame.TransformRevision == operation.Target.TransformRevision
            && string.Equals(before.Frame.SourceId, operation.Target.TargetWindowSourceId, StringComparison.Ordinal),
            "Beforeのframeが操作束縛と一致しません。");

        var after = operation.After;
        ArgumentNullException.ThrowIfNull(after);
        RequireSchema(after.SchemaVersion, nameof(operation));

        var comparison = operation.Comparison;
        ArgumentNullException.ThrowIfNull(comparison);
        RequireSchema(comparison.SchemaVersion, nameof(operation));
        Require(Enum.IsDefined(comparison.Judgement), "未知のGameTransitionJudgementです。");
        Require(
            string.Equals(comparison.BeforeObservationId, before.ObservationId, StringComparison.Ordinal),
            "ComparisonのBeforeが操作前観測と一致しません。");
        Require(
            string.Equals(comparison.AfterObservationId, after.StableScene?.ObservationId, StringComparison.Ordinal),
            "ComparisonのAfterが安定後観測と一致しません。");
    }

    private static void RequireNormalizedPoint(IReadOnlyList<double> point, string name)
    {
        Require(point.Count == 2, $"{name}は2要素です。");
        foreach (var value in point)
        {
            Require(
                double.IsFinite(value) && value is >= 0.0 and <= 1.0,
                $"{name}はclient frameへ正規化した0〜1の値です。");
        }
    }

    private static void RequireExactPayload(DemonstrationEventDraft draft)
    {
        var present =
            (draft.Operation is null ? 0 : 1)
            + (draft.FocusChange is null ? 0 : 1)
            + (draft.Stop is null ? 0 : 1);
        Require(present == 1, "eventのpayloadはちょうど一つです。");

        var matched = draft.Kind switch
        {
            DemonstrationEventKind.Operation => draft.Operation is not null,
            DemonstrationEventKind.FocusLost or DemonstrationEventKind.FocusRegained => draft.FocusChange is not null,
            DemonstrationEventKind.Stopped => draft.Stop is not null,
            _ => false,
        };
        Require(matched, "eventのpayloadがKindと一致しません。");
    }

    private static void RequireSchema(string schemaVersion, string parameterName)
    {
        if (!string.Equals(schemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"操作デモ原本のschema '{schemaVersion}' は未対応です。", parameterName);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new ArgumentException(message);
        }
    }
}
