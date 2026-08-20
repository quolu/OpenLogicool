using OpenLogicool.Domain;
using Xunit;

namespace OpenLogicool.Input.Tests;

public sealed class TimedMacroTests
{
    private static TimedMacroDefinition Definition(
        TimedMacroMode mode,
        int delayMs = 20,
        int repeatCount = 0) =>
        new(["Key:A"], delayMs, intervalMs: 10, mode, repeatCount);

    [Fact]
    public void Repeat_while_held_waits_then_stops_on_release_without_future_action()
    {
        var macro = new TimedMacro(Definition(TimedMacroMode.RepeatWhileHeld));

        macro.Activate(100);
        Assert.Equal(TimedMacroState.Waiting, macro.State);
        Assert.Empty(macro.AdvanceTo(119));
        Assert.Single(macro.AdvanceTo(120));
        Assert.Equal(TimedMacroState.Running, macro.State);

        macro.Release(121);
        Assert.Equal(TimedMacroState.Idle, macro.State);
        Assert.Empty(macro.AdvanceTo(1000));
    }

    [Fact]
    public void Toggle_explicitly_starts_and_stops_repetition()
    {
        var macro = new TimedMacro(Definition(TimedMacroMode.Toggle, delayMs: 0));

        macro.Activate(10);
        Assert.Single(macro.AdvanceTo(10));
        macro.Activate(11);

        Assert.Equal(TimedMacroState.Idle, macro.State);
        Assert.Empty(macro.AdvanceTo(100));
    }

    [Fact]
    public void Finite_repeat_emits_exact_count_and_never_catches_up()
    {
        var macro = new TimedMacro(Definition(TimedMacroMode.FiniteRepeat, delayMs: 20, repeatCount: 2));

        macro.Activate(0);
        Assert.Single(macro.AdvanceTo(25));
        Assert.Empty(macro.AdvanceTo(34));
        Assert.Single(macro.AdvanceTo(35));
        Assert.Equal(TimedMacroState.Idle, macro.State);
        Assert.Empty(macro.AdvanceTo(1000));
    }

    [Fact]
    public void Stop_prevents_future_actions_until_explicit_resume()
    {
        var macro = new TimedMacro(Definition(TimedMacroMode.Toggle, delayMs: 0));

        macro.Activate(0);
        macro.Stop();
        Assert.Equal(TimedMacroState.Stopped, macro.State);
        Assert.Empty(macro.AdvanceTo(100));
        Assert.Throws<InvalidOperationException>(() => macro.Activate(101));

        macro.Resume();
        macro.Activate(102);
        Assert.Single(macro.AdvanceTo(102));
    }

    [Fact]
    public void Profile_application_rejects_mixed_timed_and_existing_output_cell()
    {
        var profile = new MappingProfile(
            "profile-r1",
            "map-r1",
            defaultLayerId: "base",
            layerIds: ["base"],
            latchSelectors: new Dictionary<string, string>(),
            holdSelectors: new Dictionary<string, string>(),
            bindings: [new MappingBinding("G1", "base", ["Key:B"])]);

        Assert.Throws<ArgumentException>(() => TimedMacro.ValidateForProfileApplication(
            profile,
            [new TimedMacroBinding("G1", "base", Definition(TimedMacroMode.Toggle))]));
    }

    [Fact]
    public void Existing_tap_sequence_is_not_a_timed_macro_output()
    {
        Assert.Throws<ArgumentException>(() => new TimedMacroDefinition(
            ["Tap:Key:A"],
            delayMs: 0,
            intervalMs: 10,
            TimedMacroMode.Toggle));
    }
}
