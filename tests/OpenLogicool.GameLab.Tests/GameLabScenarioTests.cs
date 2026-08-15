using System.IO;
using OpenLogicool.GameLab;
using Xunit;

namespace OpenLogicool.GameLab.Tests;

public sealed class GameLabScenarioTests
{
    [Fact]
    public void Same_seed_and_scenario_produce_identical_oracle_events()
    {
        var scenario = LoadBasicScenario();

        var first = ScenarioRunner.Run(scenario);
        var second = ScenarioRunner.Run(scenario);

        Assert.Equal(first.Oracle, second.Oracle);
    }

    [Fact]
    public void Daily_reset_clears_claim_state_at_a_virtual_day_boundary()
    {
        var scenario = LoadBasicScenario();
        var machine = ExecuteActions(scenario);
        Assert.True(machine.RewardClaimed);

        machine.Tick(scenario.VirtualClock.DayLengthMs);

        Assert.False(machine.RewardClaimed);
        Assert.Equal(GameLabStateId.MainMenu, machine.CurrentState);
        Assert.Equal("auto:daily-reset", machine.Oracle[^1].Cause);
    }

    [Fact]
    public void Claim_done_has_no_reverse_transition()
    {
        Assert.False(GameLabStateMachine.HasTransition(GameLabStateId.ClaimDone, "OpenRewards"));
        Assert.False(GameLabStateMachine.HasTransition(GameLabStateId.ClaimDone, "Cancel"));
    }

    [Fact]
    public void Same_day_claim_done_rejects_another_claim_path()
    {
        var machine = ExecuteActions(LoadBasicScenario());

        Assert.True(machine.RewardClaimed);
        Assert.False(machine.TryAction("OpenRewards"));
        Assert.Equal(GameLabStateId.ClaimDone, machine.CurrentState);
    }

    [Fact]
    public void Manual_intervention_is_recorded_by_the_oracle()
    {
        var machine = new GameLabStateMachine(LoadBasicScenario());

        machine.ManualIntervention();

        Assert.Equal("manual-intervention", machine.Oracle[^1].Cause);
        Assert.Equal(GameLabStateId.MainMenu, machine.CurrentState);
    }

    [Fact]
    public void Basic_claim_scenario_matches_its_expected_event_sequence()
    {
        var scenario = LoadBasicScenario();
        var result = ScenarioRunner.Run(scenario);

        Assert.True(result.MatchesExpectedEventSequence);
        Assert.Equal(scenario.IrreversibleEffectCount, result.IrreversibleEffectCount);
    }

    [Fact]
    public void Unknown_glitch_scenario_locks_the_seeded_unknown_event_and_runner_manual_intervention()
    {
        var scenario = LoadUnknownGlitchScenario();

        var result = ScenarioRunner.Run(scenario);

        Assert.True(result.MatchesExpectedEventSequence);
        Assert.Contains(result.Oracle, entry => entry.Cause == "auto:unknown-glitch");
        Assert.Equal("manual-intervention", result.Oracle[^1].Cause);
    }

    [Fact]
    public void Loader_rejects_an_unsupported_scenario_schema()
    {
        var path = Path.Combine(RepositoryRoot(), "tests", "OpenLogicool.GameLab.Tests", "Fixtures", "scenario-invalid-schema.json");

        Assert.Throws<InvalidDataException>(() => ScenarioManifestLoader.Load(path));
    }

    private static GameLabStateMachine ExecuteActions(ScenarioManifest scenario)
    {
        var machine = new GameLabStateMachine(scenario);
        foreach (var action in scenario.AllowedActions)
        {
            Assert.True(machine.TryAction(action));
            machine.Tick(scenario.Delay.TransitionDelayMs);
        }

        return machine;
    }

    private static ScenarioManifest LoadBasicScenario() =>
        ScenarioManifestLoader.Load(Path.Combine(RepositoryRoot(), "fixtures", "scenarios", "scenario-basic-claim.v1.json"));

    private static ScenarioManifest LoadUnknownGlitchScenario() =>
        ScenarioManifestLoader.Load(Path.Combine(RepositoryRoot(), "fixtures", "scenarios", "scenario-unknown-glitch.v1.json"));

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenLogicool.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("OpenLogicool.sln を含む repository root を特定できません。");
    }
}
