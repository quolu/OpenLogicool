namespace OpenLogicool.Contracts.Playbooks;

public enum PersonalKnowledgeEvidenceLevel
{
    WebHypothesis,
    GameObserved,
    Replayed,
    Verified,
}

public sealed record PersonalLearningStep(
    string StepId,
    string SourceLabel,
    string ActionLabel,
    string TargetHint,
    string ExpectedOutcomeLabel,
    string? StructureEdgeId,
    IReadOnlyList<string> EvidenceReferences,
    PersonalKnowledgeEvidenceLevel EvidenceLevel);

public sealed record PersonalLearningNegativeExample(
    string ExampleId,
    string TargetHint,
    string ObservedDestination,
    string Reason,
    IReadOnlyList<string> EvidenceReferences);

/// <summary>
/// 実ゲームで得たroute知識をgame専用コードへ焼かずに取り込む文書。
/// StructureEdgeIdが無いstepは探索hintであり、Visual Macroへ直接昇格しない。
/// </summary>
public sealed record PersonalLearningKnowledgeDocument(
    string SchemaVersion,
    string RecordId,
    string GameId,
    string EnvironmentScope,
    string Goal,
    string InputRoute,
    IReadOnlyList<string> ProhibitedRiskTags,
    IReadOnlyList<PersonalLearningStep> Steps,
    IReadOnlyList<PersonalLearningNegativeExample> NegativeExamples,
    IReadOnlyList<string> OutcomeFacts,
    IReadOnlyList<string> SourceReferences,
    DateTimeOffset CreatedUtc);
