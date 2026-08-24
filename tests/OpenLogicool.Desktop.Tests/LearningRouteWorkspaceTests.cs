using Xunit;

namespace OpenLogicool.Desktop.Tests;

public sealed class LearningRouteWorkspaceTests
{
    [Fact]
    public void Journey_loads_saves_and_undoes_through_one_intent()
    {
        var intent = new FakeIntent();
        var workspace = new LearningRouteWorkspace(intent);
        var scope = Assert.Single(workspace.ListScopes());
        var current = workspace.Load(scope);

        var saved = workspace.Save(scope, current, "日課を完了", ["edge-1"], "短い経路へ修正");
        _ = workspace.Compile(scope, saved);
        _ = workspace.Undo(scope, saved);

        Assert.Equal(["list", "load", "save:edge-1:短い経路へ修正", "compile:route-1:version-1", "undo:route-1:version-1"], intent.Calls);
    }

    [Fact]
    public void Save_requires_goal_and_at_least_one_step_before_calling_intent()
    {
        var intent = new FakeIntent();
        var workspace = new LearningRouteWorkspace(intent);
        var scope = Assert.Single(workspace.ListScopes());
        var current = workspace.Load(scope);

        Assert.Throws<ArgumentException>(() => workspace.Save(scope, current, " ", [], "理由"));
        Assert.Equal(["list", "load"], intent.Calls);
    }

    private sealed class FakeIntent : ILearningRouteIntents
    {
        public List<string> Calls { get; } = [];

        public IReadOnlyList<LearningRouteScopeOption> ListScopes()
        {
            Calls.Add("list");
            return [new LearningRouteScopeOption("game-1", "env-1")];
        }

        public LearningRouteScreenSnapshot Load(string gameId, string environmentScope)
        {
            Calls.Add("load");
            return Snapshot(null, null, 0, false);
        }

        public LearningRouteScreenSnapshot Save(LearningRouteSaveRequest request)
        {
            Calls.Add($"save:{string.Join(',', request.EdgeIds)}:{request.UserInstruction}");
            return Snapshot("route-1", "version-1", 1, true);
        }

        public LearningRouteScreenSnapshot Undo(string gameId, string environmentScope, string routeId, string currentVersionId)
        {
            Calls.Add($"undo:{routeId}:{currentVersionId}");
            return Snapshot(routeId, "version-2", 2, true);
        }

        public LearningRouteScreenSnapshot Compile(string gameId, string environmentScope, string routeId, string currentVersionId)
        {
            Calls.Add($"compile:{routeId}:{currentVersionId}");
            return Snapshot(routeId, currentVersionId, 1, true) with { MacroStateLabel = "教師付きで生成済み" };
        }

        private static LearningRouteScreenSnapshot Snapshot(
            string? routeId,
            string? versionId,
            long revisionNumber,
            bool canUndo)
        {
            var edge = new LearningRouteEdgeItem(
                "edge-1", "ロビー", "日課一覧", "クリック", "右上の日課", "日課一覧を表示", "再現済み", "なし");
            return new LearningRouteScreenSnapshot(
                "game-1", "env-1", "structure-1", routeId, versionId, revisionNumber,
                "日課を完了", "この順序で保存", [edge], [new LearningRouteStepItem(1, edge)],
                "保存済み", "未生成", "未実行", canUndo);
        }
    }
}
