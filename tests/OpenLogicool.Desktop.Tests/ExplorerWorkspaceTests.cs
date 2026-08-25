using Xunit;

namespace OpenLogicool.Desktop.Tests;

public sealed class ExplorerWorkspaceTests
{
    [Fact]
    public void Journey_UsesOneIntentForLoadPauseStepAbandonAndCorrection()
    {
        var intent = new FakeIntent();
        var workspace = new ExplorerWorkspace(intent);
        var scope = Assert.Single(workspace.ListScopes());

        _ = workspace.Load(scope);
        _ = workspace.Pause(scope);
        _ = workspace.Step(scope);
        _ = workspace.Abandon(scope);
        _ = workspace.Correct(scope, new ExplorerLabelCorrection("state-1", "ロビー", "画面を見て訂正"));

        Assert.Equal(["list", "load", "pause", "step", "abandon", "correct:state-1:ロビー:画面を見て訂正"], intent.Calls);
    }

    [Fact]
    public void CorrectionRequiresTargetLabelAndReasonBeforeCallingIntent()
    {
        var intent = new FakeIntent();
        var workspace = new ExplorerWorkspace(intent);
        var scope = Assert.Single(workspace.ListScopes());

        var error = Assert.Throws<ArgumentException>(() =>
            workspace.Correct(scope, new ExplorerLabelCorrection("state-1", " ", "理由")));

        Assert.Contains("すべて入力", error.Message, StringComparison.Ordinal);
        Assert.Equal(["list"], intent.Calls);
    }

    private sealed class FakeIntent : IExplorerIntents
    {
        public List<string> Calls { get; } = [];

        public IReadOnlyList<ExplorerScopeOption> ListScopes()
        {
            Calls.Add("list");
            return [new ExplorerScopeOption("game-1", "env-1")];
        }

        public ExplorerScreenSnapshot Load(string gameId, string environmentScope) => Call("load");

        public ExplorerScreenSnapshot Pause(string gameId, string environmentScope) => Call("pause");

        public ExplorerScreenSnapshot Step(string gameId, string environmentScope) => Call("step");

        public ExplorerScreenSnapshot Abandon(string gameId, string environmentScope) => Call("abandon");

        public ExplorerScreenSnapshot Correct(string gameId, string environmentScope, ExplorerLabelCorrection correction) =>
            Call($"correct:{correction.StateId}:{correction.NewLabel}:{correction.Reason}");

        private ExplorerScreenSnapshot Call(string name)
        {
            Calls.Add(name);
            return new ExplorerScreenSnapshot(
                "game-1", "env-1", "structure:1", 0, 0, [], "なし", "なし",
                0, 0, 0, [], "停止なし", new ExplorerVerificationCounts(0, 0, 0, 0), [],
                false, false, false, false);
        }
    }
}
