using System.Text.Json;
using System.Text.Json.Serialization;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Playbooks;

/// <summary>game非依存のPersonal Learning Knowledge JSONを検証して読み込む。</summary>
public static class PersonalLearningKnowledgeImporter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public static PersonalLearningKnowledgeDocument Parse(string documentJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentJson);
        var document = JsonSerializer.Deserialize<PersonalLearningKnowledgeDocument>(documentJson, Json)
            ?? throw new ArgumentException("Personal Learning Knowledgeがnullです。", nameof(documentJson));
        Validate(document);
        return document;
    }

    private static void Validate(PersonalLearningKnowledgeDocument document)
    {
        if (!string.Equals(document.SchemaVersion, ContractSchemaVersions.Revision03, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(document.RecordId)
            || string.IsNullOrWhiteSpace(document.GameId)
            || string.IsNullOrWhiteSpace(document.EnvironmentScope)
            || string.IsNullOrWhiteSpace(document.Goal)
            || string.IsNullOrWhiteSpace(document.InputRoute)
            || document.ProhibitedRiskTags is null
            || document.ProhibitedRiskTags.Count == 0
            || document.ProhibitedRiskTags.Any(string.IsNullOrWhiteSpace)
            || document.Steps is null
            || document.Steps.Count == 0
            || document.Steps.Any(IsInvalid)
            || document.NegativeExamples is null
            || document.NegativeExamples.Any(IsInvalid)
            || document.OutcomeFacts is null
            || document.SourceReferences is null
            || document.SourceReferences.Count == 0
            || document.SourceReferences.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Personal Learning Knowledgeの必須fieldまたはschemaが不正です。", nameof(document));
        }
    }

    private static bool IsInvalid(PersonalLearningStep step) =>
        step is null
        || string.IsNullOrWhiteSpace(step.StepId)
        || string.IsNullOrWhiteSpace(step.SourceLabel)
        || string.IsNullOrWhiteSpace(step.ActionLabel)
        || string.IsNullOrWhiteSpace(step.TargetHint)
        || string.IsNullOrWhiteSpace(step.ExpectedOutcomeLabel)
        || step.EvidenceReferences is null
        || step.EvidenceReferences.Count == 0
        || step.EvidenceReferences.Any(string.IsNullOrWhiteSpace)
        || !Enum.IsDefined(step.EvidenceLevel);

    private static bool IsInvalid(PersonalLearningNegativeExample example) =>
        example is null
        || string.IsNullOrWhiteSpace(example.ExampleId)
        || string.IsNullOrWhiteSpace(example.TargetHint)
        || string.IsNullOrWhiteSpace(example.ObservedDestination)
        || string.IsNullOrWhiteSpace(example.Reason)
        || example.EvidenceReferences is null
        || example.EvidenceReferences.Count == 0
        || example.EvidenceReferences.Any(string.IsNullOrWhiteSpace);
}
