using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Profiles;
using Xunit;

namespace OpenLogicool.Profiles.Tests;

public sealed class WorkspaceUndoTests
{
    private static WorkspaceRevisionRecord Revision(long number) =>
        new(
            number,
            SavedAtUtc: "2026-08-16T00:00:00Z",
            new WorkspaceDocument(
                ContractSchemaVersions.Revision01,
                WorkspaceId: "ws",
                ProfileRevision: $"rev-{number}",
                MappingRevision: "map-1",
                Actions: [],
                Devices: [],
                Bindings: []));

    [Fact]
    public void Default_target_is_the_revision_before_the_latest()
    {
        var target = WorkspaceUndo.SelectTarget([Revision(1), Revision(2), Revision(3)], null);

        Assert.Equal(2, target.RevisionNumber);
    }

    [Fact]
    public void Explicit_revision_number_selects_that_revision()
    {
        var target = WorkspaceUndo.SelectTarget([Revision(1), Revision(2), Revision(3)], 1);

        Assert.Equal(1, target.RevisionNumber);
    }

    [Fact]
    public void Missing_revision_number_is_an_error_not_a_fallback()
    {
        Assert.Throws<InvalidOperationException>(() =>
            WorkspaceUndo.SelectTarget([Revision(1), Revision(2)], 9));
    }

    [Fact]
    public void Single_or_empty_history_has_no_default_undo_target()
    {
        Assert.Throws<InvalidOperationException>(() => WorkspaceUndo.SelectTarget([Revision(1)], null));
        Assert.Throws<InvalidOperationException>(() => WorkspaceUndo.SelectTarget([], null));
        Assert.Throws<InvalidOperationException>(() => WorkspaceUndo.SelectTarget([], 1));
    }
}
