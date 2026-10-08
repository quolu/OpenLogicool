using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class VisualKeyAssistTests
{
    [Fact]
    public void Inhibit_image_wins_over_both_the_key_cue_and_an_expired_timer()
    {
        var schedule = new VisualKeyAssistSchedule(0, () => 9_100);
        Assert.Equal(VisualKeyAssistDecision.Hold, schedule.Decide(15_000, inhibited: true, cue: true));
        Assert.Equal(VisualKeyAssistDecision.Hold, schedule.Decide(15_000, inhibited: true, cue: false));
        Assert.Equal(VisualKeyAssistDecision.Cue, schedule.Decide(15_000, inhibited: false, cue: true));
    }

    [Fact]
    public void No_images_uses_each_new_random_deadline_after_the_actual_input()
    {
        var intervals = new Queue<int>([8_200, 11_700]);
        var schedule = new VisualKeyAssistSchedule(0, intervals.Dequeue);
        Assert.Equal(VisualKeyAssistDecision.Wait, schedule.Decide(8_199, false, false));
        Assert.Equal(VisualKeyAssistDecision.Timed, schedule.Decide(8_200, false, false));
        schedule.RecordInput(8_350);
        Assert.Equal(VisualKeyAssistDecision.Wait, schedule.Decide(20_049, false, false));
        Assert.Equal(VisualKeyAssistDecision.Timed, schedule.Decide(20_050, false, false));
    }

    [Fact]
    public void Visible_key_cue_does_not_wait_for_the_random_timer_or_repeat_on_the_same_immediate_capture()
    {
        var schedule = new VisualKeyAssistSchedule(0, () => 12_000);
        Assert.Equal(VisualKeyAssistDecision.Cue, schedule.Decide(200, false, true));
        schedule.RecordInput(200);
        Assert.Equal(VisualKeyAssistDecision.Wait, schedule.Decide(400, false, true));
        Assert.Equal(VisualKeyAssistDecision.Cue, schedule.Decide(950, false, true));
        Assert.Equal(VisualKeyAssistDecision.Hold, schedule.Decide(950, true, true));
    }

    [Fact]
    public void User_template_is_found_at_an_offset_but_not_in_a_different_image()
    {
        const int size = 30;
        var reference = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var offset = (y * size + x) * 4;
            reference[offset] = 80;
            reference[offset + 1] = 190;
            reference[offset + 2] = 100;
            reference[offset + 3] = 255;
            if (x is >= 9 and < 21 && y is >= 9 and < 21)
                reference.AsSpan(offset, 3).Fill(245);
        }
        var template = new VisualKeyTemplate(size, size, reference);
        var target = new byte[90 * 90 * 4];
        for (var y = 0; y < size; y++)
            reference.AsSpan(y * size * 4, size * 4).CopyTo(target.AsSpan(((y + 42) * 90 + 39) * 4));
        var match = template.Find(Frame(target), [0, 0, 1, 1]);
        Assert.True(match.Matches);
        Assert.Equal(0, match.Difference);
        Assert.False(template.Find(Frame(new byte[target.Length]), [0, 0, 1, 1]).Matches);
        Assert.False(template.Find(Frame(target), [0, 0, 0.4, 0.4]).Matches);
    }

    [Fact]
    public void Cue_text_matches_spaces_inserted_by_Japanese_OCR_but_not_an_unrelated_message()
    {
        Assert.True(VisualKeyAssistRuntime.ContainsCue("確認 Space", "Space"));
        Assert.True(VisualKeyAssistRuntime.ContainsCue("画 面 を 押 し て く だ さ い", "画面を押してください"));
        Assert.False(VisualKeyAssistRuntime.ContainsCue("レベルアップボーナスを選択してください", "画面を押してください"));
    }

    private static CapturedFrame Frame(byte[] pixels) => new(
        ContractSchemaVersions.Revision03, "template-test", CaptureBackend.WindowsGraphicsCapture,
        1, 0, DateTimeOffset.UnixEpoch, 90, 90, "BGRA8", 96, 96, 1, 0, 0,
        Pixels: new FramePixels(pixels, 90 * 4));
}
