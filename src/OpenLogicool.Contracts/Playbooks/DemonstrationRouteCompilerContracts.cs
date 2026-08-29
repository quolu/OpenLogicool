using System.Security.Cryptography;
using System.Text;

namespace OpenLogicool.Contracts.Playbooks;

/// <summary>Demonstrationの1操作をLearning Routeへ採否した理由の種別。</summary>
public enum DemonstrationRouteDecisionKind
{
    Accepted,
    ExcludedStayed,
    ExcludedUndetermined,
    ExcludedDuplicate,
    ExcludedDetour,
}

/// <summary>
/// 操作デモ原本の1操作に対する採否とその理由。
/// EdgeIdはGame Structureへcommitできた場合（Judgement=Moved）だけ入る。
/// </summary>
public sealed record DemonstrationRouteDecision(
    string OperationId,
    DemonstrationRouteDecisionKind Kind,
    string Reason,
    string? EdgeId);

/// <summary>操作デモ原本1件をLearning Route新版1件へ導出した結果。</summary>
public sealed record DemonstrationRouteCompilationResult(
    string SessionId,
    LearningRouteRevision Route,
    IReadOnlyList<DemonstrationRouteDecision> Decisions);

/// <summary>
/// 目的routeのidをgame／environment／goal単位で決定的に作る。
/// OpenLogicool.Host.PurposeLearningRouteIds.Createと同じ式（層の都合で複製。
/// 入力が同じなら出力も同じになるため、demonstration由来のrouteはAI探索由来の
/// 既存route revisionへそのまま合流する）。
/// </summary>
public static class DemonstrationGoalRouteIds
{
    public static string Create(string gameId, string environmentScope, string goal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentScope);
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);
        var material = $"{gameId}\n{environmentScope}\n{goal.Normalize(NormalizationForm.FormKC).Trim()}";
        return $"purpose:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant()}";
    }
}
