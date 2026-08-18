using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Playbooks;

/// <summary>
/// Playbook の訂正（PB-008）。旧 Version は書き換えず、ParentVersionId 付きの新 Version を返す。
/// </summary>
public static class PlaybookCorrection
{
    public static PlaybookVersion Revise(
        PlaybookVersion current,
        string newVersionId,
        IReadOnlyList<PlaybookNode> nodes,
        IReadOnlyList<PlaybookEdge> edges,
        string changeReason)
    {
        ArgumentNullException.ThrowIfNull(current);
        _ = PlaybookMaterializer.ToGraph(current);

        if (string.IsNullOrWhiteSpace(newVersionId))
        {
            throw new ArgumentException("新しい VersionId が空です。", nameof(newVersionId));
        }

        if (string.Equals(newVersionId, current.VersionId, StringComparison.Ordinal))
        {
            throw new ArgumentException("訂正は新 VersionId を必要とします。", nameof(newVersionId));
        }

        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        var revised = new PlaybookVersion(
            ContractSchemaVersions.Revision01,
            newVersionId,
            current.VersionId,
            CopyNodes(nodes),
            CopyEdges(edges),
            changeReason);

        _ = PlaybookMaterializer.ToGraph(revised);
        return revised;
    }

    private static IReadOnlyList<PlaybookNode> CopyNodes(IReadOnlyList<PlaybookNode> nodes)
    {
        var copy = new PlaybookNode[nodes.Count];
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            copy[index] = node with
            {
                Preconditions = [.. node.Preconditions],
                ExpectedOutcomes = [.. node.ExpectedOutcomes],
            };
        }

        return copy;
    }

    private static IReadOnlyList<PlaybookEdge> CopyEdges(IReadOnlyList<PlaybookEdge> edges)
    {
        var copy = new PlaybookEdge[edges.Count];
        for (var index = 0; index < edges.Count; index++)
        {
            copy[index] = edges[index];
        }

        return copy;
    }
}
