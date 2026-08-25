using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Desktop;
using OpenLogicool.Probe;
using Xunit;

namespace OpenLogicool.Probe.Tests;

public sealed class SupervisedMacroLiveSmokeTests
{
    [Fact]
    public void Self_run_advances_each_ready_step_once_until_completed()
    {
        var intents = new FakeIntents(
            Snapshot(SupervisedMacroRunState.ReadyToDispatch, 1, 2),
            Snapshot(SupervisedMacroRunState.ReadyToDispatch, 2, 2),
            Snapshot(SupervisedMacroRunState.Completed, 2, 2));

        var snapshots = SupervisedMacroLiveSmoke.RunToTerminal(
            intents, "game", "env", "route", "version");

        Assert.Equal(2, intents.NextCount);
        Assert.Equal(SupervisedMacroRunState.Completed, snapshots[^1].State);
    }

    [Fact]
    public void Self_run_refuses_to_retry_the_same_ready_step()
    {
        var intents = new FakeIntents(
            Snapshot(SupervisedMacroRunState.ReadyToDispatch, 1, 2),
            Snapshot(SupervisedMacroRunState.ReadyToDispatch, 1, 2));

        Assert.Throws<InvalidOperationException>(() => SupervisedMacroLiveSmoke.RunToTerminal(
            intents, "game", "env", "route", "version"));
        Assert.Equal(1, intents.NextCount);
    }

    private static SupervisedMacroRunSnapshot Snapshot(
        SupervisedMacroRunState state,
        int sequence,
        int total) => new(
            "run:1",
            new SupervisedMacroRunPin("program", "version", "structure", "game", "env"),
            state,
            SupervisedMacroStopReason.None,
            sequence,
            total,
            state.ToString(),
            []);

    private sealed class FakeIntents(params SupervisedMacroRunSnapshot[] snapshots) : ISupervisedMacroIntents
    {
        private int index;
        public int NextCount { get; private set; }
        public SupervisedMacroRunSnapshot Start(string gameId, string environmentScope, string routeId, string routeVersionId) => snapshots[index++];
        public SupervisedMacroRunSnapshot Next() { NextCount++; return snapshots[index++]; }
        public SupervisedMacroRunSnapshot Stop() => throw new NotSupportedException();
    }
}
