using OpenLogicool.Contracts.Profiles;

namespace OpenLogicool.Desktop;

/// <summary>編集対象 workspace の読み込み結果。保存済み revision が無ければ既定下書き（RevisionNumber は null）。</summary>
public sealed record WorkspaceLoadResult(WorkspaceDocument Document, long? RevisionNumber, IReadOnlyList<WorkspaceStageCell> Stages);

/// <summary>
/// compile 1回分の結果（副作用なし・保存しない）。IsValid=false のときは ErrorMessage が非 null で、
/// 呼び出し側（Window）は保存ボタンを無効化してこの全文を表示する（設計 §2.1 の4）。
/// </summary>
public sealed record WorkspaceCompileOutcome(bool IsValid, int ProfileCount, IReadOnlyList<string> Warnings, string? ErrorMessage);

/// <summary>保存1回分の結果。「適用完了」という語をここでも使わない（APP-007）。</summary>
public sealed record WorkspaceSaveOutcome(long RevisionNumber, IReadOnlyList<WorkspaceStageCell> Stages);

/// <summary>undo1回分の結果（過去 revision を新 revision として再適用した結果の document と段階）。</summary>
public sealed record WorkspaceUndoOutcome(WorkspaceDocument Document, long RevisionNumber, IReadOnlyList<WorkspaceStageCell> Stages);

/// <summary>
/// Binding editor の I/O 境界（設計 docs/ui-design-phase3.md §3.1・§3.6）。
/// Desktop の参照は Contracts + Domain だけ（architecture test 固定）のため、SQLite・
/// WorkspaceCompiler・WorkspaceUndo の呼び出しは実装（Host）側に置く——Desktop は intent 越しに使うだけ。
/// </summary>
public interface IWorkspaceEditorIntents
{
    /// <summary>編集対象 app の workspace 文書を読み込む。</summary>
    WorkspaceLoadResult LoadDocument(string applicationFullPath);

    /// <summary>document を compile する（副作用なし）。</summary>
    WorkspaceCompileOutcome Compile(WorkspaceDocument document);

    /// <summary>
    /// document を新 revision として保存し、編集対象（applicationFullPath。共通設定は "*"）との
    /// 関連付けも同時に確定する（呼び出し前に Compile が成立している前提。失敗時は例外）。
    /// </summary>
    WorkspaceSaveOutcome Save(WorkspaceDocument document, string applicationFullPath);

    /// <summary>過去 revision（revisionNumber が null なら最新の一つ前）を新 revision として再適用する。</summary>
    WorkspaceUndoOutcome Undo(string workspaceId, long? revisionNumber);
}
