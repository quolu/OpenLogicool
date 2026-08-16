using System.IO;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Desktop;
using OpenLogicool.Input;
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

        var workspaceId = TryReverseWorkspaceId(profileIdByKind);
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
        var proposedWorkspaceId = workspaceId ?? ProposeWorkspaceId(applicationFullPath);
        return new WorkspaceLoadResult(WorkspaceDocumentEditor.CreateDraft(proposedWorkspaceId), null, BuildStages(savedRevisionNumber: null));
    }

    private static IReadOnlyList<WorkspaceStageCell> BuildStages(long? savedRevisionNumber) =>
        WorkspaceApplyReport.Build(savedRevisionNumber, WorkspaceRevisionSaver.IsHostResident())
            .Select(stage => new WorkspaceStageCell(stage.Stage, stage.State, stage.Detail))
            .ToArray();

    public WorkspaceCompileOutcome Compile(WorkspaceDocument document)
    {
        try
        {
            ValidateOutputTokens(document);
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
        ValidateOutputTokens(document);
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

    /// <summary>
    /// 出力 token の文法検証（設計 §2.1「未知 token は OutputTokens.Parse が例外にするので、UI は
    /// 握りつぶさず出す」）。WorkspaceCompiler は構造（衝突・未知 action 等）しか見ないため、
    /// token 自体の文法はここで OutputTokens（唯一の正）を直接呼んで検証する——語彙の複製はしない。
    /// </summary>
    private static void ValidateOutputTokens(WorkspaceDocument document)
    {
        foreach (var action in document.Actions)
        {
            if (action.Outputs.Count == 0)
            {
                continue;
            }

            var isSequence = OutputTokens.IsSequenceStep(action.Outputs[0]);
            foreach (var token in action.Outputs)
            {
                if (OutputTokens.IsSequenceStep(token) != isSequence)
                {
                    throw new ArgumentException(
                        $"action '{action.ActionId}' の出力で sequence 段（{OutputTokens.SequenceStepPrefix}…）と押下保持 token が混在しています。");
                }

                if (isSequence)
                {
                    OutputTokens.SplitSequenceStep(token);
                }
                else
                {
                    OutputTokens.Parse(token);
                }
            }
        }
    }

    /// <summary>ProfileId = "{WorkspaceId}-{DeviceKind}" の接頭辞から WorkspaceId を逆引きする。
    /// device 種別間で不一致、またはこの規約に従わない ProfileId があれば逆引き不能として null。</summary>
    private static string? TryReverseWorkspaceId(IReadOnlyDictionary<string, string> profileIdByKind)
    {
        string? workspaceId = null;
        foreach (var (deviceKind, profileId) in profileIdByKind)
        {
            var suffix = $"-{deviceKind}";
            if (!profileId.EndsWith(suffix, StringComparison.Ordinal))
            {
                return null;
            }

            var candidate = profileId[..^suffix.Length];
            if (workspaceId is null)
            {
                workspaceId = candidate;
            }
            else if (workspaceId != candidate)
            {
                return null;
            }
        }

        return workspaceId;
    }

    /// <summary>逆引き不能な app のための新規 WorkspaceId 提案（rail の選択名から作る最小規則）。</summary>
    private static string ProposeWorkspaceId(string applicationFullPath)
    {
        if (applicationFullPath == AppProfileResolver.DefaultMarker)
        {
            return "default";
        }

        var baseName = Path.GetFileNameWithoutExtension(applicationFullPath);
        var slugChars = baseName.ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();
        var slug = new string(slugChars).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Length == 0 ? "workspace" : $"ws-{slug}";
    }
}
