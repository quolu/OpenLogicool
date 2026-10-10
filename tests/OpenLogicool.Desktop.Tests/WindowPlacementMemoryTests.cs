using System.Windows;
using System.Windows.Interop;
using Xunit;

namespace OpenLogicool.Desktop.Tests;

public sealed class WindowPlacementMemoryTests
{
    [Fact]
    public void Window_reopens_at_the_remembered_placement_and_saves_where_it_was_closed()
    {
        RunSta(() =>
        {
            var remembered = new WindowPlacement(140, 110, 760, 590, false);
            var saved = new Dictionary<string, WindowPlacement>();
            var memory = new WindowPlacementMemory(
                key => key == "test-window" ? remembered : null,
                (key, placement) => saved[key] = placement);

            var window = new Window { Width = 300, Height = 200 };
            memory.Attach(window, "test-window");
            new WindowInteropHelper(window).EnsureHandle();
            window.Close();

            Assert.Equal(remembered, Assert.Single(saved).Value);
            Assert.Equal("test-window", saved.Keys.Single());
        });
    }

    [Fact]
    public void Window_without_a_remembered_placement_keeps_its_default_size_and_is_saved_on_close()
    {
        RunSta(() =>
        {
            var saved = new List<WindowPlacement>();
            var memory = new WindowPlacementMemory(_ => null, (_, placement) => saved.Add(placement));

            var window = new Window { Width = 480, Height = 320, Left = 60, Top = 70 };
            memory.Attach(window, "test-window");
            new WindowInteropHelper(window).EnsureHandle();
            window.Close();

            var placement = Assert.Single(saved);
            Assert.False(placement.Maximized);
            Assert.True(placement.Right > placement.Left && placement.Bottom > placement.Top);
        });
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
