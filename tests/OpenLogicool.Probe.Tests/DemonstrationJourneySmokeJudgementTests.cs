using OpenLogicool.Probe;
using Xunit;

namespace OpenLogicool.Probe.Tests;

/// <summary>
/// t07: 一巡probeの判定を、live実行なしで固定する。
/// 判定は観測だけから決まる純関数なので、保存済みJSONへ同じ関数を当てて結論を再現できる。
/// </summary>
public sealed class DemonstrationJourneySmokeJudgementTests
{
    [Fact]
    public void A_complete_journey_passes_every_check()
    {
        var checks = DemonstrationJourneySmokeJudgement.Evaluate(Complete());

        Assert.All(checks, check => Assert.True(check.Passed, $"{check.Name}: {check.Detail}"));
        Assert.Equal(10, checks.Count);
    }

    [Fact]
    public void A_journey_that_recorded_nothing_fails_instead_of_passing_quietly()
    {
        var observation = Complete() with
        {
            RecordedOperationCount = 0,
            RouteId = null,
            StepCount = 0,
            AssignedToken = null,
            Reopen = null,
        };

        var checks = DemonstrationJourneySmokeJudgement.Evaluate(observation);

        Assert.Contains(checks, check => check.Name == "物理clickが1操作として記録された" && !check.Passed);
        Assert.Contains(checks, check => check.Name == "原本からrouteが導出された" && !check.Passed);
        Assert.Contains(checks, check => check.Name == "別processで再openできた" && !check.Passed);
        Assert.DoesNotContain(checks, check => check.Name == "tokenがデモ由来routeへ解決する");
    }

    [Fact]
    public void A_reopen_that_lost_one_device_binding_fails()
    {
        var observation = Complete();
        observation = observation with
        {
            Reopen = observation.Reopen! with { BoundControlIds = ["G1"] },
        };

        var checks = DemonstrationJourneySmokeJudgement.Evaluate(observation);

        Assert.Contains(checks, check => check.Name == "G13とG600の2 bindingが残っている" && !check.Passed);
    }

    [Fact]
    public void A_reopen_that_resolves_a_different_route_fails()
    {
        var observation = Complete();
        observation = observation with
        {
            Reopen = observation.Reopen! with { RouteId = "route:someone-else" },
        };

        var checks = DemonstrationJourneySmokeJudgement.Evaluate(observation);

        Assert.Contains(checks, check => check.Name == "tokenがデモ由来routeへ解決する" && !check.Passed);
    }

    [Fact]
    public void A_cursor_that_never_reached_the_target_fails()
    {
        var checks = DemonstrationJourneySmokeJudgement.Evaluate(Complete() with { NanoCursorMatched = false });

        Assert.Contains(checks, check => check.Name == "Nanoのカーソルが対象点へ届いた" && !check.Passed);
    }

    private static JourneyObservation Complete() => new(
        "OpenLogicool.Probe",
        120, 120, 704, 481,
        NanoCursorMatched: true,
        RecordedOperationCount: 1,
        SessionState: "Stopped",
        RouteId: "route:demo",
        Goal: "self-windowを一度クリックする",
        StepCount: 1,
        AssignedToken: "Macro:free:cm91dGU6ZGVtbw:latest",
        Reopen: new ReopenObservation(
            "Macro:free:cm91dGU6ZGVtbw:latest",
            ["G1", "G9"],
            ["ws-demonstration-journey-G13", "ws-demonstration-journey-G600"],
            "route:demo",
            1));
}
