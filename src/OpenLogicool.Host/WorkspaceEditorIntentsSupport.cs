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
