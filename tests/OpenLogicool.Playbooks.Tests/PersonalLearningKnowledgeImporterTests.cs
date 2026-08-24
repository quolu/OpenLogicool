using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class PersonalLearningKnowledgeImporterTests
{
    [Fact]
    public void Phase10_nikke_record_imports_as_generic_observed_knowledge()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "knowledge", "nikke-daily-phase10.v1.json");

        var document = PersonalLearningKnowledgeImporter.Parse(File.ReadAllText(path));

        Assert.Equal("nikke-global-jp", document.GameId);
        Assert.Equal("nano-serial-hid", document.InputRoute);
        Assert.Equal(5, document.Steps.Count);
        Assert.Equal(3, document.NegativeExamples.Count);
        Assert.All(document.Steps, step => Assert.Equal(PersonalKnowledgeEvidenceLevel.GameObserved, step.EvidenceLevel));
        Assert.All(document.Steps, step => Assert.Null(step.StructureEdgeId));
        Assert.Contains("spend-premium-currency", document.ProhibitedRiskTags);
    }

    [Fact]
    public void Import_rejects_document_without_hard_policy()
    {
        const string invalid =
            """
            {"SchemaVersion":"0.3.0","RecordId":"x","GameId":"g","EnvironmentScope":"e","Goal":"goal","InputRoute":"nano","ProhibitedRiskTags":[],"Steps":[],"NegativeExamples":[],"OutcomeFacts":[],"SourceReferences":[],"CreatedUtc":"2026-08-24T00:00:00Z"}
            """;

        Assert.Throws<ArgumentException>(() => PersonalLearningKnowledgeImporter.Parse(invalid));
    }
}
