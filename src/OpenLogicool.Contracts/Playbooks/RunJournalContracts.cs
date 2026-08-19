namespace OpenLogicool.Contracts.Playbooks;

/// <summary>
/// journal に保存できる RunEvent の payload type 閉集合（PB-006）。
/// 観測・提案・承認・dispatch・結果・確定・訂正・手動介入の8種だけを受け入れ、
/// 未知の種別は保存せず拒否する。
/// </summary>
public static class RunEventPayloadTypes
{
    public const string Observation = "observation";
    public const string Proposal = "proposal";
    public const string Approval = "approval";
    public const string Dispatch = "dispatch";
    public const string DispatchResult = "dispatch-result";
    public const string Confirmation = "confirmation";
    public const string Correction = "correction";
    public const string ManualIntervention = "manual-intervention";

    public static IReadOnlyList<string> All { get; } =
        [Observation, Proposal, Approval, Dispatch, DispatchResult, Confirmation, Correction, ManualIntervention];

    public static bool IsKnown(string payloadType) => All.Contains(payloadType, StringComparer.Ordinal);
}

/// <summary>
/// 期限切れ Run の削除 preview 1行。削除経路は Run 単位であり、preview は削除しない
/// （Data Flow Contract: 期限切れは preview してから削除）。
/// </summary>
public sealed record ExpiredRunPreview(string RunId, DateTimeOffset LastPersistedUtc, long EventCount);

/// <summary>
/// append-only Execution Journal の永続化 port（PB-006、OPS-008）。
/// 上書き・部分削除の口を持たない。削除は Run 単位だけ（Data Flow Contract の削除経路）。
/// </summary>
public interface IRunJournalStore
{
    /// <summary>event を追記する。既存 (runId, runSequence)・既存 eventId への再追記は例外。</summary>
    void Append(RunEvent runEvent);

    /// <summary>run の全 event を runSequence 昇順で返す。未知 schema version は例外。</summary>
    IReadOnlyList<RunEvent> ReadRun(string runId);

    IReadOnlyList<string> ListRunIds();

    /// <summary>
    /// 最終 persisted が retention を超えた Run を列挙する。削除は行わない。
    /// retentionDays は 1〜365（Data Flow Contract。「削除するまで」は本 API を呼ばないことで表す）。
    /// </summary>
    IReadOnlyList<ExpiredRunPreview> PreviewExpiredRuns(DateTimeOffset asOfUtc, int retentionDays);

    /// <summary>指定 Run の全 event を削除する（Run 単位削除）。</summary>
    void DeleteRun(string runId);
}

/// <summary>
/// engineering log の1行（OPS-009）。correlation ID で journal の一遷移を追跡するための
/// 相関情報だけを持ち、payload 本文の field を持たない——OCR／prompt／journal 本文が
/// engineering log へ流れないことを型で保証する（Data Flow Contract）。
/// </summary>
public sealed record EngineeringLogEntry(
    string SchemaVersion,
    DateTimeOffset PersistedUtc,
    string CorrelationId,
    string CausationId,
    string RunId,
    long RunSequence,
    string EventId,
    string PayloadType);

/// <summary>journal とは別の保存先へ engineering log を書く port（OPS-009 の分離）。</summary>
public interface IEngineeringLogSink
{
    void Record(EngineeringLogEntry entry);
}
