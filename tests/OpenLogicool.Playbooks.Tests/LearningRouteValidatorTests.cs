using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class LearningRouteValidatorTests
{
    [Fact]
    public void Matching_contiguous_route_is_accepted()
    {
        LearningRouteValidator.Validate(Route(["edge:open", "edge:back"]), Revision());
    }

    [Fact]
    public void Compatible_older_revision_is_accepted_but_environment_and_discontinuity_are_rejected()
    {
        LearningRouteValidator.Validate(
            Route(["edge:open"]) with { StructureRevisionId = "structure:old" },
            Revision());
        Assert.Throws<InvalidOperationException>(() =>
            LearningRouteValidator.Validate(Route(["edge:open"]) with { EnvironmentScope = "other" }, Revision()));
        Assert.Throws<InvalidOperationException>(() =>
            LearningRouteValidator.Validate(Route(["edge:open", "edge:open"]), Revision()));
    }

    private static LearningRouteRevision Route(IReadOnlyList<string> edgeIds) => new(
        ContractSchemaVersions.Revision03,
        "route-1",
        1,
        "route:v1",
        null,
        "nikke",
        "windows11-ja/nikke-live",
        "structure:live:v2",
        "日課を完了する",
        edgeIds,
        LearningRouteAuthor.Ai,
        null,
        "探索結果から作成",
        LearningRouteStatus.Draft,
        DateTimeOffset.UnixEpoch);

    private static GameStructureRevision Revision() => new(
        ContractSchemaVersions.Revision03,
        "structure:live:v2",
        "structure:live:v1",
        2,
        new StructureScreenGraph(
            ContractSchemaVersions.Revision03,
            "graph:v2",
            [Node("state:source"), Node("state:destination")],
            [
                Edge("edge:open", "state:source", "state:destination"),
                Edge("edge:back", "state:destination", "state:source"),
            ],
            [],
            "windows11-ja/nikke-live"),
        [],
        [],
        "windows11-ja/nikke-live",
        DateTimeOffset.UnixEpoch);

    private static StructureScreenNode Node(string id) => new(
        ContractSchemaVersions.Revision03,
        id,
        "windows11-ja/nikke-live",
        [$"signature:{id}"],
        [],
        [$"evidence:{id}"],
        id,
        StructureVerificationState.Replayed);

    private static StructureScreenEdge Edge(string id, string source, string destination) => new(
        ContractSchemaVersions.Revision03,
        id,
        source,
        destination,
        null,
        $"affordance:{id}",
        "locator:v1",
        "click",
        "supervised",
        [],
        true,
        $"before:{id}",
        $"after:{id}",
        new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 300, 10000),
        [new StructureOutcomeCount(ExplorationOutcomeKind.Destination, 1)],
        [$"evidence:{id}"],
        StructureVerificationState.Replayed);
}
