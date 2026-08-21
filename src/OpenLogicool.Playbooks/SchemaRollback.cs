using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Playbooks;

/// <summary>更新対象となる durable contract の境界。</summary>
public enum SchemaBoundary
{
    Playbook,
    RunJournal,
    KnowledgePack,
}

/// <summary>一つの durable contract に対する schema 更新の宣言。</summary>
public sealed record SchemaChange(
    SchemaBoundary Boundary,
    string SourceVersion,
    string TargetVersion);

/// <summary>
/// release が適用する schema 更新と、その逆向き rollback の材料。
/// 値そのものを変換・保存しない。既存の materializer、journal、Knowledge Pack validator が
/// 各自のデータ境界を引き続き所有する。
/// </summary>
public sealed record SchemaUpdatePlan(IReadOnlyList<SchemaChange> Changes);

/// <summary>
/// Playbook／journal／Knowledge Pack の schema release 境界。
/// 未知 version は読み飛ばさず、update 計画の作成・rollback のどちらでも fail する。
/// </summary>
public static class SchemaRollback
{
    private static readonly IReadOnlyDictionary<SchemaBoundary, IReadOnlySet<string>> SupportedVersions =
        new Dictionary<SchemaBoundary, IReadOnlySet<string>>
        {
            [SchemaBoundary.Playbook] = Versions(ContractSchemaVersions.Revision01),
            [SchemaBoundary.RunJournal] = Versions(ContractSchemaVersions.Revision01),
            [SchemaBoundary.KnowledgePack] = Versions(ContractSchemaVersions.Revision01),
        };

    public static SchemaUpdatePlan Plan(IEnumerable<SchemaChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var entries = changes.ToArray();
        if (entries.Length == 0)
        {
            throw new ArgumentException("schema update には少なくとも一つの変更が必要です。", nameof(changes));
        }

        if (entries.Select(change => change.Boundary).Distinct().Count() != entries.Length)
        {
            throw new ArgumentException("一つの schema 境界を同じ update に重複して含められません。", nameof(changes));
        }

        foreach (var change in entries)
        {
            Validate(change);
        }

        return new SchemaUpdatePlan(entries);
    }

    /// <summary>
    /// update 計画の逆方向を返す。保存済みデータの書換えは行わず、呼び出し側が既存の
    /// store と validator を通して適用するための rollback 口だけを提供する。
    /// </summary>
    public static IReadOnlyList<SchemaChange> Rollback(SchemaUpdatePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(plan.Changes);

        foreach (var change in plan.Changes)
        {
            Validate(change);
        }

        return plan.Changes
            .Reverse()
            .Select(change => new SchemaChange(change.Boundary, change.TargetVersion, change.SourceVersion))
            .ToArray();
    }

    private static IReadOnlySet<string> Versions(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);

    private static void Validate(SchemaChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (!SupportedVersions.TryGetValue(change.Boundary, out var versions) ||
            !versions.Contains(change.SourceVersion) ||
            !versions.Contains(change.TargetVersion))
        {
            throw new ArgumentException(
                $"{change.Boundary} の schema version '{change.SourceVersion}' → '{change.TargetVersion}' は未対応です。",
                nameof(change));
        }
    }
}
