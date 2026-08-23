using System.IO;
using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Desktop;
using OpenLogicool.Input;
using OpenLogicool.Profiles;

namespace OpenLogicool.Host;

/// <summary>
/// <see cref="IWorkspaceEditorIntents"/> の実装間で共有する pure helper（t10: UI test scenario の
/// fake/real contract 一致を検証するため、<see cref="HostWorkspaceEditorIntents"/>（実 SQLite）と
/// <see cref="FakeWorkspaceEditorIntents"/>（in-memory・テスト用）の両方がここを呼ぶ——
/// 「一致」を通すためだけの別ロジックを作らず、ロジックそのものを一本化する）。
/// </summary>
internal static class WorkspaceEditorIntentsSupport
{
    /// <summary>
    /// 保存1回分で書くべき関連付け（editor の編集対象→保存 profile）を組み立てる。
    /// applicationFullPath が null（undo の再適用）なら編集対象の関連付けは書かない。
    /// 併せて「これまで種別に profile が1つだけ→自動で既定」で解決していた種別へ2つ目の
    /// profile が加わる場合、従来どおり解決されるよう既存 profile を明示の既定として書き残す
    /// （保存が既存の適用先を黙って変えない）。既に既定があれば何もしない。
    /// </summary>
    public static IReadOnlyList<AppProfileAssociation> BuildAssociationUpserts(
        IReadOnlyList<AppProfileAssociation> existingAssociations,
        IReadOnlyList<MappingProfileDocument> prospectiveDocuments,
        WorkspaceCompilation compilation,
        string? applicationFullPath)
    {
        var upserts = new List<AppProfileAssociation>();
        if (applicationFullPath is not null)
        {
            var path = applicationFullPath == AppProfileResolver.DefaultMarker
                ? AppProfileResolver.DefaultMarker
                : AppProfileResolver.NormalizePath(applicationFullPath);
            foreach (var profile in compilation.Profiles)
            {
                upserts.Add(new AppProfileAssociation(
                    OpenLogicool.Contracts.Shared.ContractSchemaVersions.Revision01, path, profile.DeviceKind, profile.ProfileId));
            }
        }

        var compiledIds = compilation.Profiles.Select(profile => profile.ProfileId).ToHashSet(StringComparer.Ordinal);
        var known = existingAssociations.Concat(upserts).ToList();
        foreach (var kindGroup in prospectiveDocuments.GroupBy(document => document.DeviceKind, StringComparer.Ordinal))
        {
            if (known.Any(association =>
                    association.ApplicationFullPath == AppProfileResolver.DefaultMarker
                    && association.DeviceKind == kindGroup.Key))
            {
                continue;
            }

            var documents = kindGroup.ToArray();
            if (documents.Length < 2)
            {
                continue;
            }

            var preExisting = documents.Where(document => !compiledIds.Contains(document.ProfileId)).ToArray();
            if (preExisting.Length == 1)
            {
                upserts.Add(new AppProfileAssociation(
                    OpenLogicool.Contracts.Shared.ContractSchemaVersions.Revision01,
                    AppProfileResolver.DefaultMarker, kindGroup.Key, preExisting[0].ProfileId));
            }
        }

        return upserts;
    }

    /// <summary>既存の関連付けへ upserts を (path, kind) 単位で適用した結果（resolver 検証用）。</summary>
    public static IReadOnlyList<AppProfileAssociation> MergeAssociations(
        IReadOnlyList<AppProfileAssociation> existingAssociations,
        IReadOnlyList<AppProfileAssociation> upserts)
    {
        var byKey = existingAssociations.ToDictionary(
            association => (association.ApplicationFullPath, association.DeviceKind));
        foreach (var association in upserts)
        {
            byKey[(association.ApplicationFullPath, association.DeviceKind)] = association;
        }

        return byKey.Values.ToArray();
    }

    /// <summary>段階4セルの語彙を WorkspaceApplyReport（Profiles）から Desktop の型へ写す。</summary>
    public static IReadOnlyList<WorkspaceStageCell> BuildStages(long? savedRevisionNumber, bool hostResident) =>
        WorkspaceApplyReport.Build(savedRevisionNumber, hostResident)
            .Select(stage => new WorkspaceStageCell(stage.Stage, stage.State, stage.Detail))
            .ToArray();

    /// <summary>
    /// 出力 token の文法検証（WorkspaceCompiler は構造しか見ないため、token 自体の文法は
    /// OutputTokens（唯一の正）を直接呼んで検証する——語彙の複製はしない）。
    /// </summary>
    public static void ValidateOutputTokens(WorkspaceDocument document)
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
    public static string? TryReverseWorkspaceId(IReadOnlyDictionary<string, string> profileIdByKind)
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

    /// <summary>
    /// 特定アプリの行が共通設定と同じprofile一式を参照しているか。
    /// この状態のdocumentをそのまま編集すると共通設定まで上書きするため、編集時はapp専用workspaceへ分岐する。
    /// </summary>
    public static bool ReusesDefaultWorkspace(
        string applicationFullPath,
        string workspaceId,
        IReadOnlyList<ApplicationWorkspaceRow> workspaces)
    {
        if (applicationFullPath == AppProfileResolver.DefaultMarker)
        {
            return false;
        }

        var defaultRow = workspaces.First(workspace =>
            workspace.ApplicationFullPath == AppProfileResolver.DefaultMarker);
        return string.Equals(
            TryReverseWorkspaceId(defaultRow.ProfileIdByKind),
            workspaceId,
            StringComparison.Ordinal);
    }

    /// <summary>共通設定を内容ごと継承し、特定アプリ専用の未保存workspaceへ分岐する。</summary>
    public static WorkspaceDocument ForkForApplication(
        WorkspaceDocument source,
        string applicationFullPath) =>
        source with { WorkspaceId = ProposeWorkspaceId(applicationFullPath) };

    /// <summary>逆引き不能な app のための新規 WorkspaceId 提案（rail の選択名から作る最小規則）。</summary>
    public static string ProposeWorkspaceId(string applicationFullPath)
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
