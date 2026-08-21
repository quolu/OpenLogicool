using OpenLogicool.Devices.G600;
using OpenLogicool.Host;
using OpenLogicool.Profiles;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class MigrationRollbackTests
{
    [Fact]
    public void Cancel_dry_run_never_starts_apply_or_changes_original_profile()
    {
        var report = new LgsXmlDryRunReport(
            [new LgsXmlDryRunLine(LgsXmlDryRunLineKind.Convertible, "profile", "G1", "候補")],
            [new LgsXmlDryRunLine(LgsXmlDryRunLineKind.Unsupported, "profile", "G2", "未対応")]);

        var result = MigrationRollback.CancelDryRun(report);

        Assert.Equal(MigrationRollbackOutcome.DryRunCancelled, result.Outcome);
        Assert.False(result.ApplyStarted);
        Assert.True(result.OriginalLgsProfilePreserved);
        Assert.Equal(1, result.ConvertibleCount);
        Assert.Equal(1, result.UnsupportedCount);
        Assert.Null(result.G600Result);
    }

    [Fact]
    public void Restore_delegates_to_existing_leftover_session_port()
    {
        var calls = 0;
        var expected = new G600LeftoverResult(
            G600LeftoverKind.Restore,
            "baseline の F3 を書き戻した。",
            Wrote: true,
            Attempts: 1,
            OpenError: null,
            ByteMatched: true);
        var rollback = new MigrationRollback(() =>
        {
            calls++;
            return expected;
        });

        var result = rollback.RestoreG600Baseline();

        Assert.Equal(1, calls);
        Assert.Equal(MigrationRollbackOutcome.G600BaselineRestored, result.Outcome);
        Assert.False(result.ApplyStarted);
        Assert.True(result.OriginalLgsProfilePreserved);
        Assert.Same(expected, result.G600Result);
    }

    [Fact]
    public void Failed_restore_is_not_reported_as_completed()
    {
        var rollback = new MigrationRollback(() => new G600LeftoverResult(
            G600LeftoverKind.RefuseNoDevice,
            "G600 の feature report を読めない。",
            Wrote: false,
            Attempts: 0,
            OpenError: null,
            ByteMatched: false));

        var result = rollback.RestoreG600Baseline();

        Assert.Equal(MigrationRollbackOutcome.G600RestoreNotCompleted, result.Outcome);
        Assert.True(result.OriginalLgsProfilePreserved);
    }
}
