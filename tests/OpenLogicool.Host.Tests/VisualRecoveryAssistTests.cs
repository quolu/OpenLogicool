using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Host;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class VisualRecoveryAssistTests
{
    private static readonly string Fixture = LocateFixture();
    private static VisualRecoveryProfile Profile() => VisualRecoveryProfile.Load(Path.Combine(Fixture, "profile.json"));

    [Fact]
    public void Recorded_food_images_have_distinct_states_and_a_longer_full_bar()
    {
        var recognizer = new VisualRecoveryRecognizer(Profile());
        var before = recognizer.Observe(Frame("before.png"));
        var after = recognizer.Observe(Frame("after.png"));
        Assert.True(before.HudVisible);
        Assert.Null(before.Problem);
        Assert.Equal(VisualFoodState.Ready, before.Food);
        Assert.Equal(VisualFoodState.Active, after.Food);
        Assert.InRange(before.HealthFraction!.Value, 0.98, 1);
        Assert.InRange(after.HealthFraction!.Value, 0.98, 1);
        Assert.InRange(after.BarWidth!.Value / (double)before.BarWidth!.Value, 1.48, 1.51);
    }

    [Theory]
    [InlineData("before.png")]
    [InlineData("after.png")]
    public void Half_and_quarter_health_use_the_whole_dynamic_frame_not_the_green_length(string image)
    {
        var recognizer = new VisualRecoveryRecognizer(Profile());
        var full = Frame(image);
        var fullWidth = recognizer.Observe(full).BarWidth!.Value;
        foreach (var fraction in new[] { 0.5, 0.25 })
        {
            var pixels = full.Pixels!.Bgra8.ToArray();
            var cutoff = 133 + (int)Math.Floor(fullWidth * fraction);
            for (var y = 125; y < 139; y++)
            for (var x = cutoff; x <= 330; x++)
            {
                if (x > 133 + fullWidth - 1) continue;
                var offset = y * full.Pixels.Stride + x * 4;
                pixels[offset] = 46;
                pixels[offset + 1] = 42;
                pixels[offset + 2] = 39;
            }
            var result = recognizer.Observe(full with { Pixels = new FramePixels(pixels, full.Pixels.Stride) });
            Assert.Equal(fullWidth, result.BarWidth);
            Assert.InRange(result.HealthFraction!.Value, fraction - 0.015, fraction + 0.015);
        }
    }

    [Fact]
    public void Hidden_hud_is_not_zero_health_and_a_different_size_is_explicit()
    {
        var recognizer = new VisualRecoveryRecognizer(Profile());
        var frame = Frame("before.png");
        var hidden = recognizer.Observe(frame with { Pixels = new FramePixels(new byte[frame.Width * frame.Height * 4], frame.Width * 4) });
        Assert.False(hidden.HudVisible);
        Assert.Null(hidden.HealthFraction);
        Assert.NotNull(recognizer.Observe(frame with { Width = 1000 }).Problem);
    }

    [Theory]
    [InlineData("window-small.png", 1808, 1051)]
    [InlineData("window-narrow.png", 1504, 1052)]
    [InlineData("window-short.png", 1506, 814)]
    public void Resized_real_windows_use_the_client_viewport_and_keep_the_ready_button(string image, int width, int height)
    {
        var observation = new VisualRecoveryRecognizer(Profile()).Observe(Frame(image), new FrameRect(1, 31, width, height));
        Assert.True(observation.HudVisible);
        Assert.Null(observation.Problem);
        Assert.Equal(VisualFoodState.Ready, observation.Food);
        Assert.InRange(observation.BarWidth!.Value, 129, 136);
        Assert.InRange(observation.HealthFraction!.Value, 0.97, 1);
    }

    [Fact]
    public void Combat_food_icon_is_found_after_other_effects_shift_it_to_the_right()
    {
        var observation = new VisualRecoveryRecognizer(Profile()).Observe(Frame("combat.png"));
        Assert.Equal(VisualFoodState.Active, observation.Food);
        Assert.Null(observation.Problem);
        Assert.InRange(observation.HealthFraction!.Value, 0.98, 1);
    }

    [Fact]
    public void Damage_flash_does_not_split_the_denominator_or_count_white_as_remaining_health()
    {
        var observation = new VisualRecoveryRecognizer(Profile()).Observe(Frame("damaged.png"));
        Assert.True(observation.HudVisible);
        Assert.Null(observation.Problem);
        Assert.Equal(132, observation.BarWidth);
        Assert.InRange(observation.HealthFraction!.Value, 0.2, 0.4);
        Assert.Equal(VisualFoodState.Ready, observation.Food);
        var match = VisualKeyTemplate.Load(Profile().FoodReadyImage).FindNativeSize(Frame("damaged.png"), Profile().FoodReadySearch);
        Assert.InRange(match.Difference, 0, 1);
        Assert.False(VisualKeyTemplate.Load(Profile().FoodReadyImage).FindNativeSize(Frame("after.png"), Profile().FoodReadySearch).Matches);
    }

    [Fact]
    public void Food_timer_shading_keeps_the_active_state_without_changing_the_ready_detection()
    {
        var recognizer = new VisualRecoveryRecognizer(Profile());
        var timer = recognizer.Observe(Frame("food-timer.png"));
        Assert.Equal(VisualFoodState.Active, timer.Food);
        Assert.Null(timer.Problem);
        Assert.Equal(1, timer.HealthFraction);
        Assert.Equal(VisualFoodState.Ready, recognizer.Observe(Frame("before.png")).Food);
    }

    [Fact]
    public void Background_resembling_the_rail_does_not_extend_the_health_denominator()
    {
        var frame = Frame("after.png");
        var bytes = frame.Pixels!.Bgra8.ToArray();
        for (var y = 138; y <= 142; y++)
        for (var x = 329; x < 390; x++)
        {
            var offset = y * frame.Pixels.Stride + x * 4;
            bytes[offset] = 50;
            bytes[offset + 1] = 47;
            bytes[offset + 2] = 45;
        }
        var result = new VisualRecoveryRecognizer(Profile()).Observe(frame with { Pixels = new FramePixels(bytes, frame.Pixels.Stride) });
        Assert.InRange(result.BarWidth!.Value, 197, 200);
        Assert.InRange(result.HealthFraction!.Value, 0.98, 1);
    }

    [Fact]
    public void Food_requires_visible_ready_state_and_observed_effect_before_any_other_input()
    {
        var schedule = new VisualRecoverySchedule(Profile());
        var now = DateTimeOffset.UnixEpoch;
        var ready = new VisualRecoveryObservation(true, 1, 133, VisualFoodState.Ready, null);
        Assert.Equal(VisualRecoveryAction.None, schedule.Decide(now, ready, true).Action);
        Assert.Equal(VisualRecoveryAction.Food, schedule.Decide(now, ready, false).Action);
        schedule.RecordAttempt(VisualRecoveryAction.Food, now, ready);
        Assert.Equal(VisualRecoveryAction.Wait, schedule.Decide(now.AddSeconds(1), ready, false).Action);
        Assert.Equal(VisualRecoveryAction.Wait, schedule.Decide(now.AddSeconds(2),
            ready with { Food = VisualFoodState.Active }, false).Action);
        Assert.Equal(VisualRecoveryAction.Review, schedule.Decide(now.AddSeconds(8), ready, false).Action);
        var active = ready with { Food = VisualFoodState.Active, BarWidth = 198 };
        Assert.Equal(VisualRecoveryAction.None, schedule.Decide(now.AddSeconds(9), active, false).Action);
        Assert.Equal(VisualRecoveryAction.Review, schedule.Decide(now.AddMinutes(1), ready, false).Action);
        Assert.Equal(VisualRecoveryAction.Food, schedule.Decide(now.AddMinutes(20), ready, false).Action);
    }

    [Fact]
    public void Potion_uses_threshold_and_persistent_cooldown_and_stops_if_result_is_unknown()
    {
        var now = DateTimeOffset.UnixEpoch;
        var low = new VisualRecoveryObservation(true, 0.5, 198, VisualFoodState.Active, null);
        var schedule = new VisualRecoverySchedule(Profile());
        Assert.Equal(VisualRecoveryAction.None, schedule.Decide(now, low with { HealthFraction = 0.51 }, false).Action);
        Assert.Equal(VisualRecoveryAction.Potion, schedule.Decide(now, low, false).Action);
        schedule.RecordAttempt(VisualRecoveryAction.Potion, now, low);
        var restarted = new VisualRecoverySchedule(Profile(), schedule.State);
        Assert.Equal(VisualRecoveryAction.Wait, restarted.Decide(now.AddSeconds(1), low, false).Action);
        Assert.Equal(VisualRecoveryAction.Review, restarted.Decide(now.AddSeconds(3), low, false).Action);
        Assert.Equal(VisualRecoveryAction.None, restarted.Decide(now.AddSeconds(4), low with { HealthFraction = 0.6 }, false).Action);
        Assert.Equal(VisualRecoveryAction.None, restarted.Decide(now.AddSeconds(9), low, false).Action);
        Assert.Equal(VisualRecoveryAction.Potion, restarted.Decide(now.AddSeconds(10), low, false).Action);
    }

    [Fact]
    public void Unknown_food_does_not_spend_food_or_block_potion_but_zero_health_stops()
    {
        var schedule = new VisualRecoverySchedule(Profile());
        var unknown = new VisualRecoveryObservation(true, 1, 133, VisualFoodState.Unknown, null);
        Assert.Equal(VisualRecoveryAction.None, schedule.Decide(DateTimeOffset.UnixEpoch, unknown, false).Action);
        Assert.Equal(VisualRecoveryAction.Potion, schedule.Decide(DateTimeOffset.UnixEpoch,
            unknown with { HealthFraction = 0.4 }, false).Action);
        Assert.Equal(VisualRecoveryAction.Review, schedule.Decide(DateTimeOffset.UnixEpoch,
            unknown with { HealthFraction = 0 }, false).Action);
        Assert.Equal(VisualRecoveryAction.None, schedule.Decide(DateTimeOffset.UnixEpoch,
            new(false, null, null, VisualFoodState.Unknown, null), false).Action);
    }

    [Fact]
    public void Progress_review_stops_space_without_stopping_the_independent_recovery_schedule()
    {
        var progress = new VisualKeyAssistProgress();
        Assert.Equal(VisualKeyAssistDecision.Timed, progress.Apply(VisualKeyAssistDecision.Timed));
        progress.Pause();
        Assert.True(progress.NeedsReview);
        Assert.Equal(VisualKeyAssistDecision.Wait, progress.Apply(VisualKeyAssistDecision.Timed));
        Assert.Equal(VisualKeyAssistDecision.Wait, progress.Apply(VisualKeyAssistDecision.Cue));
        Assert.Equal(VisualKeyAssistDecision.Hold, progress.Apply(VisualKeyAssistDecision.Hold));
        var low = new VisualRecoveryObservation(true, 0.4, 132, VisualFoodState.Unknown, null);
        Assert.Equal(VisualRecoveryAction.Potion, new VisualRecoverySchedule(Profile()).Decide(DateTimeOffset.UnixEpoch, low, false).Action);
    }

    [Fact]
    public void Unreadable_health_waits_without_input_then_recovers_or_stops_with_the_problem()
    {
        var schedule = new VisualRecoverySchedule(Profile());
        var now = DateTimeOffset.UnixEpoch;
        var flashing = new VisualRecoveryObservation(true, null, null, VisualFoodState.Unknown, "HPバーの枠を識別できません。");
        Assert.Equal(VisualRecoveryAction.Wait, schedule.Decide(now, flashing, false).Action);
        Assert.Equal(VisualRecoveryAction.Wait, schedule.Decide(now.AddMilliseconds(1500), flashing, false).Action);
        var low = flashing with { HealthFraction = 0.4, BarWidth = 198, Problem = null };
        Assert.Equal(VisualRecoveryAction.Potion, schedule.Decide(now.AddMilliseconds(1600), low, false).Action);
        Assert.Equal(VisualRecoveryAction.Wait, schedule.Decide(now.AddSeconds(2), flashing, false).Action);
        Assert.Equal(VisualRecoveryAction.Review, schedule.Decide(now.AddSeconds(4), flashing, false).Action);
        Assert.Equal(VisualRecoveryAction.Review, schedule.Decide(now, flashing with { HudVisible = false }, false).Action);
    }

    private static CapturedFrame Frame(string name)
    {
        using var stream = File.OpenRead(Path.Combine(Fixture, name));
        var bitmap = new FormatConvertedBitmap(BitmapDecoder.Create(stream,
            BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0);
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        return new(ContractSchemaVersions.Revision03, "recovery-test", CaptureBackend.WindowsGraphicsCapture,
            1, 0, DateTimeOffset.UnixEpoch, bitmap.PixelWidth, bitmap.PixelHeight, "BGRA8", 96, 96, 1, 0, 0,
            Pixels: new FramePixels(bytes, bitmap.PixelWidth * 4));
    }

    private static string LocateFixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "fixtures", "visual-recovery", "mabinogi-20261008");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("回復判定の実画面fixtureがありません。");
    }
}
