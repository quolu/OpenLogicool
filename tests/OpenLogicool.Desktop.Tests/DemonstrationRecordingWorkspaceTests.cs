using OpenLogicool.Contracts.Playbooks;
using Xunit;

namespace OpenLogicool.Desktop.Tests;

public sealed class DemonstrationRecordingWorkspaceTests
{
    [Fact]
    public async Task Start_trims_the_goal_and_delegates_to_intents()
    {
        var intents = new FakeIntents();
        var workspace = new DemonstrationRecordingWorkspace(intents);

        var summary = await workspace.StartAsync("  アークを開く  ");

        Assert.Equal("アークを開く", intents.StartedGoal);
        Assert.Equal("demo:1", summary.SessionId);
    }

    [Fact]
    public async Task Start_refuses_a_blank_goal_without_calling_intents()
    {
        var intents = new FakeIntents();
        var workspace = new DemonstrationRecordingWorkspace(intents);

        await Assert.ThrowsAsync<ArgumentException>(() => workspace.StartAsync("   "));

        Assert.Null(intents.StartedGoal);
    }

    [Fact]
    public void List_and_create_pass_through_to_intents_unchanged()
    {
        var intents = new FakeIntents();
        var workspace = new DemonstrationRecordingWorkspace(intents);

        Assert.Equal(intents.Sessions, workspace.ListSessions());
        Assert.Equal(intents.Steps, workspace.ListSteps("demo:1"));
        Assert.Equal("demo:1", workspace.CreateMacroFromSession("demo:1").RouteId);
    }

    private sealed class FakeIntents : IDemonstrationRecordingIntents
    {
        public string? StartedGoal { get; private set; }
        public IReadOnlyList<DemonstrationSessionSummary> Sessions { get; } =
        [
            new("demo:1", "アークを開く", "game", "env", DemonstrationSessionState.Stopped, 1, DateTimeOffset.UnixEpoch),
        ];
        public IReadOnlyList<DemonstrationStepSummary> Steps { get; } =
        [
            new(1, "クリック", "画面が変わった"),
        ];

        public Task<DemonstrationSessionSummary> StartAsync(string goal, CancellationToken cancellationToken = default)
        {
            StartedGoal = goal;
            return Task.FromResult(Sessions[0]);
        }

        public Task<DemonstrationSessionSummary> StopAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Sessions[0]);

        public DemonstrationRecordingStatus Status() =>
            new(DemonstrationRecorderStatus.Idle, null, 0, 0, 0, 0, 0);

        public IReadOnlyList<DemonstrationSessionSummary> ListSessions() => Sessions;

        public IReadOnlyList<DemonstrationStepSummary> ListSteps(string sessionId) => Steps;

        public MacroCatalogItem CreateMacroFromSession(string sessionId) =>
            new(sessionId, "v1", "game", "env", "アークを開く", 1, 1, "保存済み");
    }
}
