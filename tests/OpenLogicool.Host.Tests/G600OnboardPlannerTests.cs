using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Devices.G600;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class G600OnboardPlannerTests
{
    private static MappingProfileDocument Document(
        IReadOnlyList<MappingBindingEntry> bindings,
        IReadOnlyList<LayerSelectorEntry>? holdSelectors = null,
        IReadOnlyList<LayerSelectorEntry>? latchSelectors = null) =>
        new(
            "1",
            "ws-G600",
            "G600",
            "r1",
            "m1",
            "base",
            ["base", "shift"],
            latchSelectors ?? [],
            holdSelectors ?? [new LayerSelectorEntry("G6", "shift")],
            bindings);

    private static G600OnboardCell CellFor(G600OnboardPlan plan, int button, bool shift) =>
        plan.Cells.Single(cell => cell.Button == button && cell.ShiftLayer == shift);

    [Fact]
    public void Key_mouse_and_modifier_chord_bindings_become_cells()
    {
        var plan = G600OnboardPlanner.Build(Document(
        [
            new MappingBindingEntry("G11", "base", ["Key:A"]),
            new MappingBindingEntry("G11", "shift", ["Key:Esc"]),
            new MappingBindingEntry("G12", "base", ["Key:LCtrl", "Key:C"]),
            new MappingBindingEntry("G13", "base", ["Mouse:Middle"]),
        ]));

        Assert.True(plan.CanApply);
        Assert.Equal(6, plan.ShiftSelectorButton);
        Assert.Equal((byte)0x04, CellFor(plan, 11, shift: false).HidKey);
        Assert.Equal((byte)0x29, CellFor(plan, 11, shift: true).HidKey);
        var chord = CellFor(plan, 12, shift: false);
        Assert.Equal((byte)0x01, chord.Modifiers); // LCtrl
        Assert.Equal((byte)0x06, chord.HidKey);    // C
        Assert.Equal((byte)0x03, CellFor(plan, 13, shift: false).MouseCode);
    }

    [Fact]
    public void Unbound_buttons_and_layers_become_explicit_empty_cells()
    {
        var plan = G600OnboardPlanner.Build(Document(
            [new MappingBindingEntry("G11", "base", ["Key:A"])]));

        // G1（左クリック固定）と G6（selector）を除く 18 button × 2層が全て明示 cell
        Assert.Equal(36, plan.Cells.Count);
        Assert.Equal((0x00, 0x00, 0x00),
            (CellFor(plan, 15, false).MouseCode, CellFor(plan, 15, false).Modifiers, CellFor(plan, 15, false).HidKey));
        // shift 層の未割当は base を写さず無動作（software runtime と同じ）
        Assert.Equal((byte)0x00, CellFor(plan, 11, shift: true).HidKey);
    }

    [Fact]
    public void Inexpressible_bindings_are_all_listed_and_refused()
    {
        var plan = G600OnboardPlanner.Build(Document(
        [
            new MappingBindingEntry("G9", "base", ["Tap:Key:A", "Tap:Key:B"]),
            new MappingBindingEntry("G10", "base", ["Key:A", "Key:B"]),
            new MappingBindingEntry("G11", "base", ["Vk:0xE8"]),
            new MappingBindingEntry("G12", "base", ["Mouse:Left", "Key:A"]),
            new MappingBindingEntry("G1", "base", ["Key:A"]),
        ]));

        Assert.False(plan.CanApply);
        Assert.Equal(5, plan.Errors.Count);
        Assert.Empty(plan.Cells);
    }

    [Fact]
    public void Latch_layers_and_multiple_hold_selectors_are_refused()
    {
        var latch = G600OnboardPlanner.Build(Document(
            [],
            latchSelectors: [new LayerSelectorEntry("G20", "shift")],
            holdSelectors: []));
        Assert.False(latch.CanApply);

        var doubleHold = G600OnboardPlanner.Build(Document(
            [],
            holdSelectors: [new LayerSelectorEntry("G6", "shift"), new LayerSelectorEntry("G7", "shift")]));
        Assert.False(doubleHold.CanApply);
    }

    [Fact]
    public void Without_hold_selector_g6_is_a_normal_button()
    {
        var plan = G600OnboardPlanner.Build(Document(
            [new MappingBindingEntry("G6", "base", ["Key:B"])],
            holdSelectors: []));

        Assert.True(plan.CanApply);
        Assert.Null(plan.ShiftSelectorButton);
        Assert.Equal((byte)0x05, CellFor(plan, 6, shift: false).HidKey);
        Assert.Equal(38, plan.Cells.Count); // G1 だけ除外＝19 button × 2層
    }
}
