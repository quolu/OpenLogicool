using System.Security.Cryptography;
using System.Text;

namespace OpenLogicool.Contracts.Playbooks;

public enum LearningRouteAuthor
{
    Ai,
    User,
    Import,
}

public enum LearningRouteStatus
{
    Draft,
    Compiled,
    Verified,
    Retired,
}

/// <summary>
/// 探索済みのStructure edge列から作る、利用者が確認・修正できる学習ルートの保存案。
/// 保存後の版は書き換えず、ParentVersionIdを指す新版として追記する。
/// </summary>
public sealed record LearningRouteDraft(
    string SchemaVersion,
    string RouteId,
    string? ParentVersionId,
    string GameId,
    string EnvironmentScope,
    string StructureRevisionId,
    string Goal,
    IReadOnlyList<string> EdgeIds,
    LearningRouteAuthor Author,
    string? UserInstruction,
    string ChangeReason,
    LearningRouteStatus Status,
    DateTimeOffset CreatedUtc,
    IReadOnlyList<DemonstrationRouteStep>? RecordedSteps = null);

public sealed record LearningRouteRevision(
    string SchemaVersion,
    string RouteId,
    long RevisionNumber,
    string VersionId,
    string? ParentVersionId,
    string GameId,
    string EnvironmentScope,
    string StructureRevisionId,
    string Goal,
    IReadOnlyList<string> EdgeIds,
    LearningRouteAuthor Author,
    string? UserInstruction,
    string ChangeReason,
    LearningRouteStatus Status,
    DateTimeOffset CreatedUtc,
    IReadOnlyList<DemonstrationRouteStep>? RecordedSteps = null)
{
    public int StepCount => RecordedSteps?.Count ?? EdgeIds.Count;
    public int PendingStepCount => RecordedSteps?.Count(step => step.ExpectedJudgement is null) ?? 0;
}

/// <summary>原本の順序と有限入力を保持する候補。利用者の期待結果と観測済みedgeは別に保存する。</summary>
public sealed record DemonstrationRouteStep(
    string SessionId,
    string OperationId,
    string Operation,
    IReadOnlyList<double>? NormalizedPoint,
    IReadOnlyList<string>? KeyTokens,
    int? VerticalScrollSteps,
    int? HorizontalScrollSteps,
    IReadOnlyList<double>? DragDestinationNormalized,
    string? EdgeId,
    OpenLogicool.Contracts.Exploration.GameTransitionJudgement? ExpectedJudgement,
    string ReviewReason,
    string? OriginalSessionId = null);

public static class LearningRouteVersionIds
{
    public static string Next(string routeId, string? parentVersionId, long revisionNumber)
    {
        var material = $"{routeId}\n{parentVersionId ?? "root"}\n{revisionNumber}";
        return $"route:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant()}";
    }
}

public interface ILearningRouteStore
{
    LearningRouteRevision Append(LearningRouteDraft draft);

    IReadOnlyList<LearningRouteRevision> ReadRevisions(string routeId);

    LearningRouteRevision? LoadLatest(string routeId);

    IReadOnlyList<string> ListRouteIds(string gameId, string environmentScope);
}
