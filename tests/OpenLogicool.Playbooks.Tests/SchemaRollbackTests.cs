using OpenLogicool.Contracts.Shared;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class SchemaRollbackTests
{
    [Fact]
    public void A_known_schema_update_has_a_reverse_rollback_for_all_durable_boundaries()
    {
        var plan = SchemaRollback.Plan(
        [
            new SchemaChange(SchemaBoundary.Playbook, ContractSchemaVersions.Revision01, ContractSchemaVersions.Revision01),
            new SchemaChange(SchemaBoundary.RunJournal, ContractSchemaVersions.Revision01, ContractSchemaVersions.Revision01),
            new SchemaChange(SchemaBoundary.KnowledgePack, ContractSchemaVersions.Revision01, ContractSchemaVersions.Revision01),
        ]);

        var rollback = SchemaRollback.Rollback(plan);

        Assert.Equal(3, rollback.Count);
        Assert.Equal(SchemaBoundary.KnowledgePack, rollback[0].Boundary);
        Assert.Equal(SchemaBoundary.RunJournal, rollback[1].Boundary);
        Assert.Equal(SchemaBoundary.Playbook, rollback[2].Boundary);
        Assert.All(rollback, change =>
        {
            Assert.Equal(ContractSchemaVersions.Revision01, change.SourceVersion);
            Assert.Equal(ContractSchemaVersions.Revision01, change.TargetVersion);
        });
    }

    [Fact]
    public void An_unknown_schema_version_fails_for_update_and_rollback()
    {
        var unknown = new SchemaChange(SchemaBoundary.Playbook, "9.9.9", ContractSchemaVersions.Revision01);

        Assert.Throws<ArgumentException>(() => SchemaRollback.Plan([unknown]));
        Assert.Throws<ArgumentException>(() => SchemaRollback.Rollback(new SchemaUpdatePlan([unknown])));
    }
}
