using System.IO;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Desktop;
using OpenLogicool.Persistence;
using OpenLogicool.Profiles;

namespace OpenLogicool.Host;

/// <summary>
/// <see cref="IWorkspaceEditorIntents"/> の実装（設計 §3.1: SQLite・WorkspaceCompiler・WorkspaceUndo の
/// 呼び出しは Host 側に置く。Desktop は intent delegate 経由でしか使わない）。
/// SQLite connection は呼び出し元（Ui() コマンド）が UI セッションの間 open したままにする——
/// 常駐 fast path は起動しないため同期呼び出しで足りる（設計 §3.1「compile は pure で軽い」）。
/// </summary>
public sealed class HostWorkspaceEditorIntents(SqliteConnection connection) : IWorkspaceEditorIntents
{
    public WorkspaceLoadResult LoadDocument(string applicationFullPath)
    {
        var documents = new SqliteMappingProfileStore(connection).ListAll();
        var associations = new SqliteAppAssociationStore(connection).ListAll();
        var resolver = AppProfileResolver.Build(documents, associations);
        var workspaces = ApplicationWorkspaceCatalog.Build(resolver, associations);

        var row = workspaces.FirstOrDefault(workspace => workspace.ApplicationFullPath == applicationFullPath);
        var profileIdByKind = row?.ProfileIdByKind ?? new Dictionary<string, string>();

        var workspaceId = WorkspaceEditorIntentsSupport.TryReverseWorkspaceId(profileIdByKind);
        if (workspaceId is not null)
        {
            var revisions = new SqliteWorkspaceRevisionStore(connection).ListRevisions(workspaceId);
            if (revisions.Count > 0)
            {
                var latest = revisions[^1];
                return new WorkspaceLoadResult(latest.Document, latest.RevisionNumber, BuildStages(latest.RevisionNumber));
            }
        }

        // 関連付け済み profile が workspace command 経由でない（直接 import 等）、または
        // 関連付けが無い app: 新規 workspace の空下書きを提案する（設計 §3.4「規則の判断に迷ったら…」の
        // 最小規則——ProfileId 接頭辞から逆引きできなければ rail の選択名から新規 WorkspaceId を提案）。
        var proposedWorkspaceId = workspaceId ?? WorkspaceEditorIntentsSupport.ProposeWorkspaceId(applicationFullPath);
        return new WorkspaceLoadResult(WorkspaceDocumentEditor.CreateDraft(proposedWorkspaceId), null, BuildStages(savedRevisionNumber: null));
    }

    private static IReadOnlyList<WorkspaceStageCell> BuildStages(long? savedRevisionNumber) =>
        WorkspaceEditorIntentsSupport.BuildStages(savedRevisionNumber, WorkspaceRevisionSaver.IsHostResident());

    public WorkspaceCompileOutcome Compile(WorkspaceDocument document)
    {
        try
        {
            WorkspaceEditorIntentsSupport.ValidateOutputTokens(document);
        }
        catch (ArgumentException error)
        {
            return new WorkspaceCompileOutcome(false, ProfileCount: 0, Warnings: [], $"出力エラー: {error.Message}");
        }

        WorkspaceCompilation compilation;
        try
        {
            compilation = WorkspaceCompiler.Compile(document);
        }
        catch (ArgumentException error)
        {
            return new WorkspaceCompileOutcome(false, ProfileCount: 0, Warnings: [], error.Message);
        }

        return new WorkspaceCompileOutcome(true, compilation.Profiles.Count, compilation.Warnings, ErrorMessage: null);
    }

    public WorkspaceSaveOutcome Save(WorkspaceDocument document)
    {
        WorkspaceEditorIntentsSupport.ValidateOutputTokens(document);
        var compilation = WorkspaceCompiler.Compile(document);

        long revisionNumber;
        try
        {
            revisionNumber = WorkspaceRevisionSaver.SaveCompilation(connection, document, compilation);
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException(
                $"workspace '{document.WorkspaceId}' を保存すると解決不能になります: {error.Message}", error);
        }

        return new WorkspaceSaveOutcome(revisionNumber, BuildStages(revisionNumber));
    }

    public WorkspaceUndoOutcome Undo(string workspaceId, long? revisionNumber)
    {
        var revisions = new SqliteWorkspaceRevisionStore(connection).ListRevisions(workspaceId);
        var target = WorkspaceUndo.SelectTarget(revisions, revisionNumber);

        var compilation = WorkspaceCompiler.Compile(target.Document);
        var newRevisionNumber = WorkspaceRevisionSaver.SaveCompilation(connection, target.Document, compilation);

        return new WorkspaceUndoOutcome(target.Document, newRevisionNumber, BuildStages(newRevisionNumber));
    }
}
