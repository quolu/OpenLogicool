using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Xunit;

namespace OpenLogicool.Desktop.Tests;

public sealed class KeyCaptureDialogTests
{
    [Fact]
    public void Escape_is_recorded_as_the_key_to_send_instead_of_closing_the_dialog()
    {
        RunSta(() =>
        {
            var dialog = new KeyCaptureDialog("回避", "（未設定）");
            var source = new HwndSource(new HwndSourceParameters("key-capture-test") { WindowStyle = 0 });
            try
            {
                dialog.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                dialog.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyUpEvent });

                var controls = Descendants(dialog).ToArray();
                Assert.Contains(controls.OfType<TextBlock>(), text => text.Text == "Esc");
                Assert.True(controls.OfType<Button>().Single(button => Equals(button.Content, "これに決める")).IsEnabled);
                Assert.Null(dialog.Result);
            }
            finally
            {
                source.Dispose();
            }
        });
    }

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
