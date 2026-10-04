using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenLogicool.Contracts.Playbooks;
using Xunit;

namespace OpenLogicool.Desktop.Tests;

public sealed class MacroStepConfirmationDialogTests
{
    [Theory]
    [InlineData("OK・次の手順へ", MacroStepDecision.Accept)]
    [InlineData("違う・ここで補正", MacroStepDecision.Correct)]
    [InlineData("中止", MacroStepDecision.Stop)]
    public void Dialog_shows_both_results_and_returns_only_the_clicked_choice(string buttonLabel, MacroStepDecision expected)
    {
        RunSta(() =>
        {
            var file = Path.Combine(Path.GetTempPath(), $"macro-confirmation-{Guid.NewGuid():N}.png");
            try
            {
                var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 255 }, 4);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(file)) encoder.Save(stream);
                var dialog = new MacroStepConfirmationDialog(new("confirm:1", 3, "クリック", "Moved", file, file, "違いがあります。"));
                MacroStepDecision? result = null; dialog.Decided += choice => result = choice;
                var controls = Descendants(dialog).ToArray();
                Assert.Equal(2, controls.OfType<Image>().Count());
                Assert.Null(result);
                var buttons = controls.OfType<Button>().ToArray();
                Assert.All(buttons, button => Assert.False(button.IsDefault));
                buttons.Single(button => Equals(button.Content, buttonLabel)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(expected, result);
            }
            finally { File.Delete(file); }
        });
    }

    [Fact]
    public void Missing_actual_image_disables_ok_and_closing_the_dialog_stops_the_playback()
    {
        RunSta(() =>
        {
            var dialog = new MacroStepConfirmationDialog(new("confirm:2", 4, "クリック", "未判定", null, null, "取得できません。"));
            MacroStepDecision? result = null; dialog.Decided += choice => result = choice;
            Assert.False(Descendants(dialog).OfType<Button>().Single(button => Equals(button.Content, "OK・次の手順へ")).IsEnabled);
            dialog.Close(); Assert.Equal(MacroStepDecision.Stop, result);
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
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw failure;
    }
}
