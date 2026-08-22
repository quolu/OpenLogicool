using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Desktop;
using OpenLogicool.Profiles;

namespace OpenLogicool.Host;

/// <summary>
/// <see cref="IWorkspaceEditorIntents"/> の in-memory fake（t10: UI test scenario の fake/real
/// contract 一致検証用）。SQLite を in-memory dictionary へ置き換えるだけで、compile・保存前の
/// 解決可能性検証・段階セル表示は <see cref="HostWorkspaceEditorIntents"/> と同じ共有関数
/// （<see cref="WorkspaceCompiler"/>・<see cref="WorkspaceEditorIntentsSupport"/>・
/// <see cref="AppProfileResolver"/>）をそのまま呼ぶ——検証専用の別ロジックは作らない。
/// hostResident は常に false（fake に常駐 host という概念は無い）。
/// </summary>
public sealed class FakeWorkspaceEditorIntents : IWorkspaceEditorIntents
{
    private readonly Dictionary<string, MappingProfileDocument> _profilesById = new(StringComparer.Ordinal);
    private readonly List<AppProfileAssociation> _associations = [];
    private readonly Dictionary<string, List<WorkspaceRevisionRecord>> _revisionsByWorkspaceId = new(StringComparer.Ordinal);

    public WorkspaceLoadResult LoadDocument(string applicationFullPath)
    {
        var resolver = AppProfileResolver.Build(_profilesById.Values.ToArray(), _associations);
        var workspaces = ApplicationWorkspaceCatalog.Build(resolver, _associations);

        var row = workspaces.FirstOrDefault(workspace => workspace.ApplicationFullPath == applicationFullPath);
        var profileIdByKind = row?.ProfileIdByKind ?? new Dictionary<string, string>();

        var workspaceId = WorkspaceEditorIntentsSupport.TryReverseWorkspaceId(profileIdByKind);
        if (workspaceId is not null &&
            _revisionsByWorkspaceId.TryGetValue(workspaceId, out var revisions) &&
            revisions.Count > 0)
        {
            var latest = revisions[^1];
            return new WorkspaceLoadResult(latest.Document, latest.RevisionNumber, BuildStages(latest.RevisionNumber));
        }

        var proposedWorkspaceId = workspaceId ?? WorkspaceEditorIntentsSupport.ProposeWorkspaceId(applicationFullPath);
        return new WorkspaceLoadResult(WorkspaceDocumentEditor.CreateDraft(proposedWorkspaceId), null, BuildStages(savedRevisionNumber: null));
    }

    private static IReadOnlyList<WorkspaceStageCell> BuildStages(long? savedRevisionNumber) =>
        WorkspaceEditorIntentsSupport.BuildStages(savedRevisionNumber, hostResident: false);

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

    public WorkspaceSaveOutcome Save(WorkspaceDocument document, string applicationFullPath)
    {
        WorkspaceEditorIntentsSupport.ValidateOutputTokens(document);
        var compilation = WorkspaceCompiler.Compile(document);

        var revisionNumber = AppendRevisionAfterResolveCheck(document, compilation, applicationFullPath);
        return new WorkspaceSaveOutcome(revisionNumber, BuildStages(revisionNumber));
    }

    public WorkspaceUndoOutcome Undo(string workspaceId, long? revisionNumber)
    {
        var revisions = _revisionsByWorkspaceId.TryGetValue(workspaceId, out var list) ? list : [];
        var target = WorkspaceUndo.SelectTarget(revisions, revisionNumber);
        var compilation = WorkspaceCompiler.Compile(target.Document);

        var newRevisionNumber = AppendRevisionAfterResolveCheck(target.Document, compilation);
        return new WorkspaceUndoOutcome(target.Document, newRevisionNumber, BuildStages(newRevisionNumber));
    }

    /// <summary>
    /// 保存前に「保存後の全体が解決可能か」を検証してから revision 追記＋profile upsert を行う
    /// （real 側の WorkspaceRevisionSaver.SaveCompilation と同じ規則——単一 SQLite transaction の
    /// 代わりに in-memory dictionary への書き込みなので、fake には巻き戻し対象の途中失敗は起きない）。
    /// </summary>
    private long AppendRevisionAfterResolveCheck(WorkspaceDocument document, WorkspaceCompilation compilation, string? applicationFullPath = null)
    {
        var compiledIds = compilation.Profiles.Select(profile => profile.ProfileId).ToHashSet(StringComparer.Ordinal);
        var prospective = _profilesById.Values
            .Where(existing => !compiledIds.Contains(existing.ProfileId))
            .Concat(compilation.Profiles)
            .ToArray();
        var associationUpserts = WorkspaceEditorIntentsSupport.BuildAssociationUpserts(
            _associations, prospective, compilation, applicationFullPath);
        var merged = WorkspaceEditorIntentsSupport.MergeAssociations(_associations, associationUpserts);
        try
        {
            AppProfileResolver.Build(prospective, merged);
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException(
                $"workspace '{document.WorkspaceId}' を保存すると解決不能になります: {error.Message}", error);
        }

        if (!_revisionsByWorkspaceId.TryGetValue(document.WorkspaceId, out var revisions))
        {
            _revisionsByWorkspaceId[document.WorkspaceId] = revisions = [];
        }

        var revisionNumber = revisions.Count == 0 ? 1 : revisions[^1].RevisionNumber + 1;
        revisions.Add(new WorkspaceRevisionRecord(revisionNumber, DateTime.UtcNow.ToString("o"), document));

        foreach (var profile in compilation.Profiles)
        {
            _profilesById[profile.ProfileId] = profile;
        }

        _associations.Clear();
        _associations.AddRange(merged);

        return revisionNumber;
    }
}
