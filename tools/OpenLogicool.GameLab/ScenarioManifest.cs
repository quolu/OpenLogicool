using System.IO;
using System.Text.Json;

namespace OpenLogicool.GameLab;

public sealed class ScenarioManifest
{
    public required string ScenarioId { get; init; }

    public required string Schema { get; init; }

    public required int Seed { get; init; }

    public required VirtualClockConfiguration VirtualClock { get; init; }

    public required string InitialState { get; init; }

    public required IReadOnlyList<string> AllowedActions { get; init; }

    public required string ExpectedFrameReference { get; init; }

    public required string ExpectedObservationReference { get; init; }

    public required IReadOnlyList<ExpectedOracleEvent> ExpectedEventSequence { get; init; }

    public required int IrreversibleEffectCount { get; init; }

    public required string ResetMethod { get; init; }

    public required ViewportConfiguration Viewport { get; init; }

    public required double Dpi { get; init; }

    public required double FrameTimeMs { get; init; }

    public required SeededChance Popup { get; init; }

    public required DelayConfiguration Delay { get; init; }

    public required SeededChance Unknown { get; init; }

    public required ManualInterventionConfiguration ManualIntervention { get; init; }

    public required string CrashPoint { get; init; }
}

public sealed class VirtualClockConfiguration
{
    public required double InitialMonotonicMs { get; init; }

    public required double DayLengthMs { get; init; }
}

public sealed class ViewportConfiguration
{
    public required int Width { get; init; }

    public required int Height { get; init; }
}

public sealed class SeededChance
{
    public required int Numerator { get; init; }

    public required int Denominator { get; init; }
}

public sealed class DelayConfiguration
{
    public required double TransitionDelayMs { get; init; }
}

public sealed class ManualInterventionConfiguration
{
    public required bool InvokeAfterActions { get; init; }
}

public sealed record ExpectedOracleEvent(
    long Seq,
    double MonotonicMs,
    string StateId,
    string Cause,
    string ScenarioId);

public static class ScenarioManifestLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static ScenarioManifest Load(string filePath) =>
        JsonSerializer.Deserialize<ScenarioManifest>(File.ReadAllText(filePath), JsonOptions)
        ?? throw new InvalidDataException($"scenario manifest '{filePath}' を読み取れません。");
}
