using System.Windows.Input;
using OpenLogicool.Desktop;
using Xunit;

namespace OpenLogicool.Desktop.Tests;

public sealed class KeyCaptureSessionTests
{
    [Fact]
    public void Released_chord_is_locked_until_explicit_reset()
    {
        var session = new KeyCaptureSession();

        session.KeyDown(Key.LeftCtrl);
        session.KeyDown(Key.C);
        session.KeyUp(Key.C);
        session.KeyUp(Key.LeftCtrl);

        Assert.True(session.IsReady);
        Assert.Equal("Key:LCtrl Key:C", session.CandidateToken);

        session.KeyDown(Key.B);
        session.KeyUp(Key.B);
        Assert.Equal("Key:LCtrl Key:C", session.CandidateToken);

        session.Reset();
        session.KeyDown(Key.B);
        session.KeyUp(Key.B);
        Assert.Equal("Key:B", session.CandidateToken);
    }

    [Fact]
    public void Only_device_input_after_candidate_completion_can_commit()
    {
        var session = new KeyCaptureSession();
        session.KeyDown(Key.A);
        session.KeyUp(Key.A);
        var readyAt = Assert.IsType<double>(session.ReadyAtMonotonicMs);

        Assert.False(session.CanCommitFromDevicePress(readyAt - 0.001));
        Assert.True(session.CanCommitFromDevicePress(readyAt));
        Assert.True(session.CanCommitFromDevicePress(readyAt + 1));
    }
}
