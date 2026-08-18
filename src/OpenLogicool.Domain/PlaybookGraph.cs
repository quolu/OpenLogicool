namespace OpenLogicool.Domain;

/// <summary>Playbook graph の node（pure）。検証は <see cref="PlaybookGraph"/> 構築子が行う。</summary>
public sealed record PlaybookGraphNode(
    string NodeId,
    bool IsEntry,
    string? StateId,
    IReadOnlyList<string> Preconditions,
    string? SemanticActionId,
    IReadOnlyList<string> ExpectedOutcomes);

/// <summary>Playbook graph の edge（分岐）。検証は <see cref="PlaybookGraph"/> 構築子が行う。</summary>
public sealed record PlaybookGraphEdge(
    string EdgeId,
    string FromNodeId,
    string ToNodeId,
    string? BranchCondition);

/// <summary>
/// 一つの Playbook version の pure graph（計画 §6.3／PB-001）。
/// 不正な graph は例外＝適用不可。到達不能 node は黙って捨てず列挙して拒否する。
/// </summary>
public sealed class PlaybookGraph
{
    public PlaybookGraph(
        string versionId,
        string? parentVersionId,
        string changeReason,
        IEnumerable<PlaybookGraphNode> nodes,
        IEnumerable<PlaybookGraphEdge> edges)
    {
        if (string.IsNullOrWhiteSpace(versionId))
        {
            throw new ArgumentException("VersionId が空です。", nameof(versionId));
        }

        if (parentVersionId is not null && string.IsNullOrWhiteSpace(parentVersionId))
        {
            throw new ArgumentException("ParentVersionId を付けるなら空にはできません。", nameof(parentVersionId));
        }

        if (parentVersionId is not null && string.Equals(parentVersionId, versionId, StringComparison.Ordinal))
        {
            throw new ArgumentException("ParentVersionId は VersionId 自身にはできません。", nameof(parentVersionId));
        }

        if (string.IsNullOrWhiteSpace(changeReason))
        {
            throw new ArgumentException("ChangeReason が空です。", nameof(changeReason));
        }

        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        var nodeList = new List<PlaybookGraphNode>();
        var nodesById = new Dictionary<string, PlaybookGraphNode>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.NodeId))
            {
                throw new ArgumentException("node id が空です。", nameof(nodes));
            }

            if (node.StateId is not null && string.IsNullOrWhiteSpace(node.StateId))
            {
                throw new ArgumentException($"node '{node.NodeId}' の StateId が空です。", nameof(nodes));
            }

            if (node.SemanticActionId is not null && string.IsNullOrWhiteSpace(node.SemanticActionId))
            {
                throw new ArgumentException($"node '{node.NodeId}' の SemanticActionId が空です。", nameof(nodes));
            }

            var copied = node with
            {
                Preconditions = CopyNamedStrings(node.Preconditions, "Preconditions", node.NodeId),
                ExpectedOutcomes = CopyNamedStrings(node.ExpectedOutcomes, "ExpectedOutcomes", node.NodeId),
            };

            if (!nodesById.TryAdd(copied.NodeId, copied))
            {
                throw new ArgumentException($"node id '{copied.NodeId}' が重複しています。", nameof(nodes));
            }

            nodeList.Add(copied);
        }

        if (nodeList.Count == 0)
        {
            throw new ArgumentException("node が一つもありません。", nameof(nodes));
        }

        var entryNodeIds = nodeList
            .Where(node => node.IsEntry)
            .Select(node => node.NodeId)
            .ToArray();
        if (entryNodeIds.Length == 0)
        {
            throw new ArgumentException("入口 node が一つもありません。", nameof(nodes));
        }

        var edgeList = new List<PlaybookGraphEdge>();
        var edgeIds = new HashSet<string>(StringComparer.Ordinal);
        var outgoing = nodeList.ToDictionary(node => node.NodeId, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (string.IsNullOrWhiteSpace(edge.EdgeId))
            {
                throw new ArgumentException("edge id が空です。", nameof(edges));
            }

            if (!edgeIds.Add(edge.EdgeId))
            {
                throw new ArgumentException($"edge id '{edge.EdgeId}' が重複しています。", nameof(edges));
            }

            if (!nodesById.ContainsKey(edge.FromNodeId))
            {
                throw new ArgumentException($"edge '{edge.EdgeId}' の FromNodeId '{edge.FromNodeId}' が存在しません。", nameof(edges));
            }

            if (!nodesById.ContainsKey(edge.ToNodeId))
            {
                throw new ArgumentException($"edge '{edge.EdgeId}' の ToNodeId '{edge.ToNodeId}' が存在しません。", nameof(edges));
            }

            var branchCondition = edge.BranchCondition is null
                ? null
                : string.IsNullOrWhiteSpace(edge.BranchCondition)
                    ? throw new ArgumentException($"edge '{edge.EdgeId}' の BranchCondition が空です。", nameof(edges))
                    : edge.BranchCondition;

            var copied = edge with { BranchCondition = branchCondition };
            edgeList.Add(copied);
            outgoing[copied.FromNodeId].Add(copied.ToNodeId);
        }

        var unreachable = FindUnreachableNodeIds(entryNodeIds, outgoing, nodesById.Keys);
        if (unreachable.Count > 0)
        {
            throw new ArgumentException(
                $"到達不能な node があります: {string.Join(", ", unreachable)}。",
                nameof(nodes));
        }

        VersionId = versionId;
        ParentVersionId = parentVersionId;
        ChangeReason = changeReason;
        Nodes = nodeList;
        Edges = edgeList;
        EntryNodeIds = Array.AsReadOnly(entryNodeIds);
    }

    public string VersionId { get; }

    public string? ParentVersionId { get; }

    public string ChangeReason { get; }

    public IReadOnlyList<PlaybookGraphNode> Nodes { get; }

    public IReadOnlyList<PlaybookGraphEdge> Edges { get; }

    public IReadOnlyList<string> EntryNodeIds { get; }

    private static IReadOnlyList<string> CopyNamedStrings(
        IReadOnlyList<string> values,
        string fieldName,
        string nodeId)
    {
        ArgumentNullException.ThrowIfNull(values);

        var copy = new string[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"{fieldName} に空の要素があります（node '{nodeId}'）。");
            }

            copy[index] = value;
        }

        return Array.AsReadOnly(copy);
    }

    private static IReadOnlyList<string> FindUnreachableNodeIds(
        IReadOnlyList<string> entryNodeIds,
        IReadOnlyDictionary<string, List<string>> outgoing,
        IReadOnlyCollection<string> allNodeIds)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        foreach (var entryNodeId in entryNodeIds)
        {
            if (reachable.Add(entryNodeId))
            {
                queue.Enqueue(entryNodeId);
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in outgoing[current])
            {
                if (reachable.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return allNodeIds
            .Where(nodeId => !reachable.Contains(nodeId))
            .OrderBy(nodeId => nodeId, StringComparer.Ordinal)
            .ToArray();
    }
}
