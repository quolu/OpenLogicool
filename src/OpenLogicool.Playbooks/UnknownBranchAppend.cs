using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Playbooks;

/// <summary>
/// 未知 branch を旧版を変えずに訂正版へだけ追記する口。
/// </summary>
public static class UnknownBranchAppend
{
    public static PlaybookVersion Append(
        PlaybookVersion verifiedVersion,
        string newVersionId,
        PlaybookNode unknownNode,
        PlaybookEdge unknownBranch,
        string changeReason)
    {
        ArgumentNullException.ThrowIfNull(verifiedVersion);
        ArgumentNullException.ThrowIfNull(unknownNode);
        ArgumentNullException.ThrowIfNull(unknownBranch);

        if (unknownNode.IsEntry)
        {
            throw new ArgumentException("未知 branch の node は入口にできません。", nameof(unknownNode));
        }

        if (!string.Equals(unknownBranch.ToNodeId, unknownNode.NodeId, StringComparison.Ordinal))
        {
            throw new ArgumentException("未知 branch は追加する node を終点にしなければなりません。", nameof(unknownBranch));
        }

        if (string.IsNullOrWhiteSpace(unknownBranch.BranchCondition))
        {
            throw new ArgumentException("未知 branch は branch condition を必要とします。", nameof(unknownBranch));
        }

        return PlaybookCorrection.Revise(
            verifiedVersion,
            newVersionId,
            [.. verifiedVersion.Nodes, unknownNode],
            [.. verifiedVersion.Edges, unknownBranch],
            changeReason);
    }
}
