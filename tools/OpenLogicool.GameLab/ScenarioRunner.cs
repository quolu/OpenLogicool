using System.IO;
using System.Text.Json;

namespace OpenLogicool.GameLab;

public sealed record ScenarioRunResult(
    IReadOnlyList<GameLabOracleEntry> Oracle,
    int IrreversibleEffectCount,
    bool MatchesExpectedEventSequence);

public static class ScenarioRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static ScenarioRunResult Run(ScenarioManifest scenario)
    {
        var machine = new GameLabStateMachine(scenario);

        foreach (var action in scenario.AllowedActions)
        {
            if (!machine.TryAction(action))
            {
                throw new InvalidOperationException($"scenario '{scenario.ScenarioId}' は action '{action}' を受理できません。");
            }

            machine.Tick(scenario.Delay.TransitionDelayMs);
        }

        if (scenario.ManualIntervention.InvokeAfterActions)
        {
            machine.ManualIntervention();
        }

        var oracle = machine.Oracle.ToArray();
        var matchesExpectedEventSequence = oracle.Select(entry => new ExpectedOracleEvent(
                entry.Seq,
                entry.MonotonicMs,
                entry.StateId,
                entry.Cause,
                entry.ScenarioId))
            .SequenceEqual(scenario.ExpectedEventSequence);

        return new ScenarioRunResult(
            oracle,
            machine.RewardClaimed ? 1 : 0,
            matchesExpectedEventSequence);
    }

    public static void WriteOracleJsonLines(string filePath, IEnumerable<GameLabOracleEntry> oracle) =>
        File.WriteAllLines(filePath, oracle.Select(entry => JsonSerializer.Serialize(entry, JsonOptions)));
}
