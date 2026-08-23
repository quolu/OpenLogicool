using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Devices.G13;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class HostG13LcdSettingsIntentTests
{
    [Fact]
    public void Text_is_rendered_to_a_persistable_g13_frame_on_sta()
    {
        WorkspaceG13LcdSetting? setting = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                setting = new HostG13LcdSettingsIntent().FromText("NIKKE");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.NotNull(setting);
        Assert.Equal(WorkspaceG13LcdContentKind.Text, setting.Kind);
        Assert.Equal("NIKKE", setting.Text);
        var frame = Convert.FromBase64String(setting.FramebufferBase64);
        Assert.Equal(G13LcdFrame.FramebufferLength, frame.Length);
        Assert.Contains(frame, value => value != 0);
    }

    [Fact]
    public void Empty_and_overlong_text_are_rejected()
    {
        var intent = new HostG13LcdSettingsIntent();

        Assert.Throws<ArgumentException>(() => intent.FromText("  "));
        Assert.Throws<ArgumentException>(() => intent.FromText(new string('x', 121)));
    }
}
