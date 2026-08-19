namespace OpenLogicool.Contracts.Playbooks;

/// <summary>
/// journal に保存できる RunEvent の payload type 閉集合（PB-006）。
/// 観測・提案・承認・dispatch・結果・確定・訂正・手動介入の8種（t03）に、
/// run 制御の skip・abandon・version-switch の3種（PB-007・§6.8・t05）と
/// fault 解決の disarm（§6.7・t07）を加えた12種だけを受け入れ、
/// 未知の種別は保存せず拒否する。pause／resume は durable な進行効果を持たないため journal 対象外
/// （再起動後に自動で走り出す経路が存在せず、記録すべき「進行の変更」が無い）。
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

    /// <summary>手順1個を実行せず飛ばした記録（§6.8「skipを別eventにする」）。NodeOrTransitionId 必須。</summary>
    public const string Skip = "skip";

    /// <summary>Run 単位の中止（PB-007）。この event 以降、同じ Run へ event は積まれない。</summary>
    public const string Abandon = "abandon";

    /// <summary>
    /// 正規の version 切替（PB-007・§6.8）。event の PlaybookVersionId が切替後の新 version を運ぶ
    /// （pin と異なる version を運んでよい唯一の event）。切替前 version は payload に記録する。
    /// </summary>
    public const string VersionSwitch = "version-switch";

    /// <summary>
    /// DispatchArmed 後、外部入力 API を一度も呼んでいないことを runtime 自身が保証できる場合だけの
    /// 中止終端の記録（§6.7 Disarmed・t07）。保証根拠は payload に記録する。AttemptId 必須。
    /// ActorType は System だけ——runtime の判定であり、利用者操作でも自動化の成功でもない。
    /// </summary>
    public const string Disarm = "disarm";

    public static IReadOnlyList<string> All { get; } =
        [Observation, Proposal, Approval, Dispatch, DispatchResult, Confirmation, Correction, ManualIntervention, Skip, Abandon, VersionSwitch, Disarm];

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
