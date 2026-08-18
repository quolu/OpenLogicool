using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Domain;

namespace OpenLogicool.Playbooks;

/// <summary>
/// PlaybookVersion（wire）と Domain の PlaybookGraph の変換。
/// 内容検証は PlaybookGraph の構築子が行う——不正な version はここで例外として現れ、
/// 黙って捨てたり既定値で埋めたりしない。
/// </summary>
public static class PlaybookMaterializer
{
    public static PlaybookGraph ToGraph(PlaybookVersion document)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnsureSchema(document.SchemaVersion, "PlaybookVersion", document.VersionId);

        if (document.Nodes is null)
        {
            throw new ArgumentException("Nodes が null です。", nameof(document));
        }

        if (document.Edges is null)
        {
            throw new ArgumentException("Edges が null です。", nameof(document));
        }

        foreach (var node in document.Nodes)
        {
            EnsureSchema(node.SchemaVersion, "PlaybookNode", node.NodeId);
        }

        foreach (var edge in document.Edges)
        {
            EnsureSchema(edge.SchemaVersion, "PlaybookEdge", edge.EdgeId);
        }

        return new PlaybookGraph(
            document.VersionId,
            document.ParentVersionId,
            document.ChangeReason,
            document.Nodes.Select(ToNode),
            document.Edges.Select(ToEdge));
    }

    private static PlaybookGraphNode ToNode(PlaybookNode node) =>
        new(
            node.NodeId,
            node.IsEntry,
            node.StateId,
            node.Preconditions,
            node.SemanticActionId,
            node.ExpectedOutcomes);

    private static PlaybookGraphEdge ToEdge(PlaybookEdge edge) =>
        new(edge.EdgeId, edge.FromNodeId, edge.ToNodeId, edge.BranchCondition);

    private static void EnsureSchema(string schemaVersion, string kind, string id)
    {
        if (schemaVersion != ContractSchemaVersions.Revision01)
        {
            throw new ArgumentException(
                $"{kind} '{id}' の schema version '{schemaVersion}' は未対応です（対応: {ContractSchemaVersions.Revision01}）。");
        }
    }
}
