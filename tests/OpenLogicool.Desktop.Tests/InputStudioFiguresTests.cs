using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using OpenLogicool.Contracts.Devices.G13;
using OpenLogicool.Contracts.Devices.G600;
using Xunit;

namespace OpenLogicool.Desktop.Tests;

public sealed class InputStudioFiguresTests
{
    private static readonly IReadOnlyDictionary<string, string> G13Layers =
        new Dictionary<string, string> { ["M1"] = "base", ["M2"] = "m2", ["M3"] = "m3" };

    [Fact]
    public void G13_figure_has_a_key_for_every_assignable_control_including_the_stick_directions()
    {
        RunSta(() =>
        {
            var clicked = new List<string>();
            var figure = InputStudioFigures.BuildG13(Request(clicked.Add), G13Layers, "base", _ => { });
            var keys = Buttons(figure.Root);

            // LCD 列は図に置いていない（いまの線画に押せる場所を重ねていない）。層切替の M1〜M3 は割当先ではない。
            var expected = G13Controls.Buttons.Where(control => !control.StartsWith("LCD", StringComparison.Ordinal) && control is not ("M1" or "M2" or "M3"));
            foreach (var controlId in expected)
            {
                var name = InputStudioFigures.G13StickName(controlId) ?? controlId;
                var key = Assert.Single(keys, button => AutomationProperties.GetName(button) == $"{name}（未割当）");
                key.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(controlId, clicked[^1]);
            }
        });
    }

    [Fact]
    public void G13_layer_keys_switch_to_their_layer_and_only_the_current_one_is_lit()
    {
        RunSta(() =>
        {
            var requested = new List<string>();
            var figure = InputStudioFigures.BuildG13(Request(_ => { }), G13Layers, "m2", requested.Add);
            var keys = Buttons(figure.Root);

            var m1 = keys.Single(button => AutomationProperties.GetName(button).StartsWith("M1：", StringComparison.Ordinal));
            var m2 = keys.Single(button => AutomationProperties.GetName(button).StartsWith("M2：", StringComparison.Ordinal));
            Assert.Null(m1.Effect);
            Assert.NotNull(m2.Effect);

            m1.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(["base"], requested);
        });
    }

    [Fact]
    public void Assigned_key_shows_the_action_and_the_selected_action_is_the_one_that_glows_strongest()
    {
        RunSta(() =>
        {
            var bindings = new Dictionary<string, InputStudioFigures.FigureBinding>
            {
                ["G4"] = new("dodge", "回避", "Space", Colors.Gold),
                ["G22"] = new("menu", "メニュー", "Esc", Colors.Coral),
            };
            var figure = InputStudioFigures.BuildG13(
                new InputStudioFigures.FigureRequest(bindings, "dodge", _ => { }, _ => { }), G13Layers, "base", _ => { });
            var keys = Buttons(figure.Root);

            var selected = keys.Single(button => AutomationProperties.GetName(button) == "G4（回避）");
            var other = keys.Single(button => AutomationProperties.GetName(button) == "G22（メニュー）");
            Assert.NotNull(selected.Effect);
            Assert.Null(other.Effect);
            Assert.Equal(new Thickness(2), selected.BorderThickness);
            Assert.Contains(Descendants(selected).OfType<TextBlock>(), text => text.Text == "Space");
        });
    }

    [Fact]
    public void G600_figure_reaches_every_control_and_keeps_g_shift_out_of_assignment_while_it_switches_layers()
    {
        RunSta(() =>
        {
            var clicked = new List<string>();
            var notices = new List<string>();
            var figure = InputStudioFigures.BuildG600(
                new InputStudioFigures.FigureRequest(new Dictionary<string, InputStudioFigures.FigureBinding>(), null, clicked.Add, notices.Add),
                shiftIsButton: false);
            var buttons = Buttons(figure.Root);

            foreach (var controlId in G600Controls.Buttons.Where(control => control != "G6"))
            {
                var label = InputStudioFigures.G600PhysicalName(controlId) is { } name ? $"{controlId}（{name}）" : controlId;
                var key = Assert.Single(buttons, button => AutomationProperties.GetName(button) == $"{label}（未割当）");
                key.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(controlId, clicked[^1]);
            }

            var before = clicked.Count;
            buttons.Single(button => AutomationProperties.GetName(button) == "対応表 G6（G-Shift）").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before, clicked.Count);
            Assert.Single(notices);
        });
    }

    private static InputStudioFigures.FigureRequest Request(Action<string> onKey) =>
        new(new Dictionary<string, InputStudioFigures.FigureBinding>(), null, onKey, _ => { });

    private static Button[] Buttons(UIElement root) => Descendants(root).OfType<Button>().ToArray();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in Descendants(child)) yield return item;
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }
}
