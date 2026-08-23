using System.IO;
using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Devices.G13;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class G13LcdDisplayFrameSelectorTests
{
    [Fact]
    public void Saved_frame_is_selected_and_missing_setting_selects_windows()
    {
        var custom = Enumerable.Repeat((byte)0x5a, G13LcdFrame.FramebufferLength).ToArray();
        var setting = new WorkspaceG13LcdSetting(
            WorkspaceG13LcdContentKind.Image,
            Convert.ToBase64String(custom),
            "game.png",
            null);
        var selected = G13LcdDisplayFrameSelector.Select(setting).ToArray();
        var windows = G13LcdDisplayFrameSelector.Select(setting: null).ToArray();

        Assert.Equal(custom, selected);
        Assert.Equal(G13LcdFrame.FramebufferLength, windows.Length);
        Assert.NotEqual(windows, selected);
    }

    [Fact]
    public void Invalid_saved_frame_is_rejected_explicitly()
    {
        var invalidBase64 = new WorkspaceG13LcdSetting(
            WorkspaceG13LcdContentKind.Image, "not-base64", "game.png", null);
        var wrongLength = invalidBase64 with { FramebufferBase64 = Convert.ToBase64String([0x01]) };

        Assert.Throws<InvalidDataException>(() => G13LcdDisplayFrameSelector.Select(invalidBase64));
        Assert.Throws<InvalidDataException>(() => G13LcdDisplayFrameSelector.Select(wrongLength));
    }
}
