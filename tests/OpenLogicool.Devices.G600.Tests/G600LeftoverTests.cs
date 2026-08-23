using OpenLogicool.Devices.G600;
using Xunit;

namespace OpenLogicool.Devices.G600.Tests;

public sealed class G600LeftoverTests
{
    private static byte[] CleanF3()
    {
        var report = new byte[G600SideRemap.ReportLength];
        for (var i = 0; i < report.Length; i++)
        {
            report[i] = (byte)(i & 0xFF);
        }

        report[0] = 0xF3;
        return report;
    }

    [Fact]
    public void Apply_is_skipped_when_g600_is_not_managed()
    {
        var decision = G600LeftoverPolicy.DecideApply(
            managed: false,
            devicePresent: true,
            coexistenceRunning: false,
            CleanF3(),
            baselineF3: null);

        Assert.Equal(G600LeftoverKind.SkipNotManaged, decision.Kind);
        Assert.False(decision.IsWrite);
    }

    [Fact]
    public void Apply_refuses_coexistence_without_writing()
    {
        var decision = G600LeftoverPolicy.DecideApply(true, true, coexistenceRunning: true, CleanF3(), null);

        Assert.Equal(G600LeftoverKind.RefuseCoexistence, decision.Kind);
        Assert.False(decision.IsWrite);
    }

    [Fact]
    public void Apply_from_clean_is_a_write()
    {
        var decision = G600LeftoverPolicy.DecideApply(true, true, false, CleanF3(), null);

        Assert.Equal(G600LeftoverKind.Apply, decision.Kind);
        Assert.True(decision.IsWrite);
    }

    [Fact]
    public void Apply_when_already_remapped_with_baseline_does_not_rewrite()
    {
        var clean = CleanF3();
        var decision = G600LeftoverPolicy.DecideApply(true, true, false, G600SideRemap.Build(clean), clean);

        Assert.Equal(G600LeftoverKind.AlreadyApplied, decision.Kind);
        Assert.False(decision.IsWrite);
        Assert.False(decision.IsHardFailure);
    }

    [Fact]
    public void Apply_when_already_remapped_without_baseline_is_a_hard_failure()
    {
        var decision = G600LeftoverPolicy.DecideApply(true, true, false, G600SideRemap.Build(CleanF3()), null);

        Assert.Equal(G600LeftoverKind.RefuseAppliedWithoutBaseline, decision.Kind);
        Assert.True(decision.IsHardFailure);
    }

    [Fact]
    public void Apply_refuses_no_output_state_without_baseline()
    {
        var noOutput = G600LegacySuppression.Build(CleanF3(), G600LegacySuppressionMode.NoOutput);

        var decision = G600LeftoverPolicy.DecideApply(
            true,
            true,
            false,
            noOutput,
            baselineF3: null,
            mode: G600LegacySuppressionMode.NoOutput);

        Assert.Equal(G600LeftoverKind.RefuseAppliedWithoutBaseline, decision.Kind);
        Assert.True(decision.IsHardFailure);
    }

    [Fact]
    public void Restore_without_baseline_is_a_skip()
    {
        var decision = G600LeftoverPolicy.DecideRestore(false, CleanF3(), null);

        Assert.Equal(G600LeftoverKind.SkipNothingToRestore, decision.Kind);
        Assert.False(decision.IsWrite);
    }

    [Fact]
    public void Restore_when_current_matches_baseline_does_not_write()
    {
        var clean = CleanF3();
        var decision = G600LeftoverPolicy.DecideRestore(false, clean, clean);

        Assert.Equal(G600LeftoverKind.AlreadyRestored, decision.Kind);
        Assert.False(decision.IsWrite);
    }

    [Fact]
    public void Restore_of_remapped_f3_is_a_write()
    {
        var clean = CleanF3();
        var decision = G600LeftoverPolicy.DecideRestore(false, G600SideRemap.Build(clean), clean);

        Assert.Equal(G600LeftoverKind.Restore, decision.Kind);
        Assert.True(decision.IsWrite);
    }

    [Fact]
    public void Evidence_write_retries_until_readback_matches()
    {
        var desired = G600SideRemap.Build(CleanF3());
        var access = new ScriptedFeatureAccess(CleanF3(), failVerifyTimes: 1);

        var result = G600EvidenceWrite.TryWrite(access, desired, _ => { }, maxAttempts: 3, settleMs: 0);

        Assert.True(result.Matched);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(desired, access.Current);
        Assert.True(access.ClosedEveryHandle);
    }

    [Fact]
    public void Evidence_write_uses_a_new_open_for_verify()
    {
        var desired = G600SideRemap.Build(CleanF3());
        var access = new ScriptedFeatureAccess(CleanF3());

        var result = G600EvidenceWrite.TryWrite(access, desired, _ => { }, maxAttempts: 1, settleMs: 0);

        Assert.True(result.Matched);
        Assert.Equal(2, access.OpenCount);
    }

    [Fact]
    public void Session_apply_saves_baseline_before_remap_and_restore_returns_it()
    {
        var clean = CleanF3();
        var directory = Path.Combine(Path.GetTempPath(), "ol-leftover-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var access = new ScriptedFeatureAccess(clean);
            var session = new G600LeftoverSession(
                access,
                new FileG600OnboardBaselineStore(directory),
                coexistenceRunning: () => false,
                sleep: _ => { },
                settleMs: 0,
                maxAttempts: 1);

            var apply = session.Apply(managed: true);
            Assert.Equal(G600LeftoverKind.Apply, apply.Kind);
            Assert.True(apply.ByteMatched);
            Assert.True(G600SideRemap.IsApplied(access.Current));
            Assert.Equal(clean, new FileG600OnboardBaselineStore(directory).LoadF3());

            var restore = session.Restore();
            Assert.Equal(G600LeftoverKind.Restore, restore.Kind);
            Assert.True(restore.ByteMatched);
            Assert.Equal(clean, access.Current);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Session_apply_does_not_write_when_not_managed()
    {
        var access = new ScriptedFeatureAccess(CleanF3());
        var session = new G600LeftoverSession(
            access,
            new MemoryBaselineStore(),
            () => false,
            _ => { },
            settleMs: 0);

        var result = session.Apply(managed: false);

        Assert.Equal(G600LeftoverKind.SkipNotManaged, result.Kind);
        Assert.Equal(0, access.OpenCount);
        Assert.False(G600SideRemap.IsApplied(access.Current));
    }

    [Fact]
    public void Session_apply_does_not_write_when_coexistence_is_running()
    {
        var access = new ScriptedFeatureAccess(CleanF3());
        var session = new G600LeftoverSession(
            access,
            new MemoryBaselineStore(),
            () => true,
            _ => { },
            settleMs: 0);

        var result = session.Apply(managed: true);

        Assert.Equal(G600LeftoverKind.RefuseCoexistence, result.Kind);
        Assert.False(result.Wrote);
    }

    [Fact]
    public void Session_second_apply_with_baseline_does_not_rewrite()
    {
        var clean = CleanF3();
        var remapped = G600SideRemap.Build(clean);
        var access = new ScriptedFeatureAccess(remapped);
        var baseline = new MemoryBaselineStore { F3 = clean };
        var session = new G600LeftoverSession(access, baseline, () => false, _ => { }, settleMs: 0);

        var result = session.Apply(managed: true);

        Assert.Equal(G600LeftoverKind.AlreadyApplied, result.Kind);
        Assert.Equal(1, access.OpenCount);
        Assert.Equal(remapped, access.Current);
    }

    [Fact]
    public void Session_serial_hid_apply_uses_no_output_suppression_and_restores_original()
    {
        var clean = CleanF3();
        var access = new ScriptedFeatureAccess(clean);
        var baseline = new MemoryBaselineStore();
        var session = new G600LeftoverSession(access, baseline, () => false, _ => { }, settleMs: 0);

        var apply = session.Apply(true, G600LegacySuppressionMode.NoOutput);

        Assert.Equal(G600LeftoverKind.Apply, apply.Kind);
        Assert.True(apply.ByteMatched);
        Assert.True(G600LegacySuppression.IsApplied(access.Current, G600LegacySuppressionMode.NoOutput));
        Assert.Equal(clean, baseline.F3);

        var restore = session.Restore();
        Assert.Equal(G600LeftoverKind.Restore, restore.Kind);
        Assert.Equal(clean, access.Current);
    }

    [Fact]
    public void Session_mode_change_after_interrupted_run_keeps_original_baseline()
    {
        var clean = CleanF3();
        var access = new ScriptedFeatureAccess(G600SideRemap.Build(clean));
        var baseline = new MemoryBaselineStore { F3 = clean };
        var session = new G600LeftoverSession(access, baseline, () => false, _ => { }, settleMs: 0);

        var apply = session.Apply(true, G600LegacySuppressionMode.NoOutput);

        Assert.Equal(G600LeftoverKind.Apply, apply.Kind);
        Assert.True(apply.ByteMatched);
        Assert.True(G600LegacySuppression.IsApplied(access.Current, G600LegacySuppressionMode.NoOutput));
        Assert.Equal(clean, baseline.F3);
    }

    [Fact]
    public void Session_external_clean_change_replaces_stale_baseline_before_apply()
    {
        var stale = CleanF3();
        var current = CleanF3();
        current[10] ^= 0x5A;
        var access = new ScriptedFeatureAccess(current);
        var baseline = new MemoryBaselineStore { F3 = stale };
        var session = new G600LeftoverSession(access, baseline, () => false, _ => { }, settleMs: 0);

        var apply = session.Apply(true, G600LegacySuppressionMode.NoOutput);

        Assert.Equal(G600LeftoverKind.Apply, apply.Kind);
        Assert.Equal(current, baseline.F3);
        var restore = session.Restore();
        Assert.Equal(G600LeftoverKind.Restore, restore.Kind);
        Assert.Equal(current, access.Current);
    }

    private sealed class MemoryBaselineStore : IG600OnboardBaselineStore
    {
        public byte[]? F3 { get; set; }

        public byte[]? LoadF3() => F3?.ToArray();

        public void SaveF3(byte[] profileF3) => F3 = profileF3.ToArray();
    }

    private sealed class ScriptedFeatureAccess : IG600FeatureAccess
    {
        private int _remainingVerifyFailures;

        public ScriptedFeatureAccess(byte[] current, int failVerifyTimes = 0)
        {
            Current = current.ToArray();
            _remainingVerifyFailures = failVerifyTimes;
        }

        public byte[] Current { get; private set; }

        public int OpenCount { get; private set; }

        public bool ClosedEveryHandle { get; private set; } = true;

        public bool TryOpen(out IG600FeatureHandle? handle)
        {
            OpenCount++;
            handle = new ScriptedHandle(this);
            return true;
        }

        private sealed class ScriptedHandle : IG600FeatureHandle
        {
            private readonly ScriptedFeatureAccess _owner;
            private bool _disposed;

            public ScriptedHandle(ScriptedFeatureAccess owner) => _owner = owner;

            public void SetFeature(byte[] report) => _owner.Current = report.ToArray();

            public byte[] GetFeature(byte reportId)
            {
                if (_owner._remainingVerifyFailures > 0)
                {
                    _owner._remainingVerifyFailures--;
                    var dirty = _owner.Current.ToArray();
                    dirty[^1] ^= 0xFF;
                    return dirty;
                }

                var copy = _owner.Current.ToArray();
                copy[0] = reportId;
                return copy;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    _owner.ClosedEveryHandle = false;
                    return;
                }

                _disposed = true;
            }
        }
    }
}
