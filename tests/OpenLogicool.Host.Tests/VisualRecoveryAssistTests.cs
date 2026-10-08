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
        Assert.Equal(0, before.WhiteFraction);
        Assert.Equal(0, after.WhiteFraction);
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
    public void 縮小で下辺の行が丸められても丸い右端と全長を取得する()
    {
        var observation = new VisualRecoveryRecognizer(Profile()).Observe(
            Frame("window-resized-rounding.png"), new FrameRect(1, 31, 1711, 1085));
        Assert.True(observation.HudVisible);
        Assert.Null(observation.Problem);
        Assert.InRange(observation.BarWidth!.Value, 129, 136);
        Assert.InRange(observation.HealthFraction!.Value, 0.97, 1);
        Assert.Equal(0, observation.WhiteFraction);
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
        Assert.Equal(VisualRecoveryAction.None, schedule.Decide(now.AddMinutes(1), ready, false).Action);
        Assert.Equal(VisualRecoveryAction.Food, schedule.Decide(now.AddMinutes(20), ready, false).Action);
    }

    [Fact]
    public void Food_ready_before_reuse_interval_does_not_end_health_monitoring_after_restart()
    {
        var now = DateTimeOffset.UnixEpoch;
        var schedule = new VisualRecoverySchedule(Profile(), new(LastFood: now));
        var ready = new VisualRecoveryObservation(true, 0.917910447761194, 134, VisualFoodState.Ready, null, 0.04477611940298507);
        Assert.Equal(VisualRecoveryAction.None, schedule.Decide(now.AddSeconds(1189), ready, false).Action);
        Assert.Equal(VisualRecoveryAction.Potion, schedule.Decide(now.AddSeconds(1190),
            ready with { HealthFraction = 0.4 }, false).Action);
        Assert.Equal(VisualRecoveryAction.Bandage, schedule.Decide(now.AddSeconds(1191),
            ready with { WhiteFraction = 0.25 }, false).Action);
        Assert.Null(schedule.State.LastPotion);
        Assert.Equal(now, schedule.State.LastFood);
    }

    [Theory]
    [InlineData("wounded.png", 0.20, 0.30)]
    [InlineData("wounded-low.png", 0.30, 0.40)]
    public void White_in_recorded_resized_health_bars_is_measured_separately_from_colored_health(string image, double minimum, double maximum)
    {
        var observation = new VisualRecoveryRecognizer(Profile()).Observe(Frame(image), new FrameRect(1, 31, 1506, 814));
        Assert.Null(observation.Problem);
        Assert.Equal(198, observation.BarWidth);
        Assert.InRange(observation.WhiteFraction!.Value, minimum, maximum);
        Assert.Equal(VisualRecoveryAction.Bandage, new VisualRecoverySchedule(Profile()).Decide(DateTimeOffset.UnixEpoch, observation, false).Action);
    }

    [Fact]
    public void Bandage_uses_twenty_percent_white_before_potion_and_keeps_its_own_persistent_cooldown()
    {
        var now = DateTimeOffset.UnixEpoch;
        var white = new VisualRecoveryObservation(true, 0.4, 198, VisualFoodState.Active, null, 0.2);
        var schedule = new VisualRecoverySchedule(Profile());
        Assert.Equal(VisualRecoveryAction.None, schedule.Decide(now, white, true).Action);
        Assert.Equal(VisualRecoveryAction.Potion, schedule.Decide(now, white with { WhiteFraction = 0.19 }, false).Action);
        Assert.Equal(VisualRecoveryAction.Bandage, schedule.Decide(now, white, false).Action);
        schedule.RecordAttempt(VisualRecoveryAction.Bandage, now, white);
        var restarted = new VisualRecoverySchedule(Profile(), schedule.State);
        Assert.Equal(VisualRecoveryAction.Wait, restarted.Decide(now.AddSeconds(1), white, false).Action);
        Assert.Equal(VisualRecoveryAction.Review, restarted.Decide(now.AddSeconds(3), white, false).Action);
        var healed = white with { WhiteFraction = 0.1, HealthFraction = 0.8 };
        Assert.Equal(VisualRecoveryAction.None, restarted.Decide(now.AddSeconds(3.5), healed, false).Action);
        Assert.False(restarted.State.BandagePending);
        Assert.Equal(VisualRecoveryAction.None, restarted.Decide(now.AddSeconds(4.9), healed with { WhiteFraction = 0.2 }, false).Action);
        Assert.Equal(VisualRecoveryAction.Bandage, restarted.Decide(now.AddSeconds(5), healed with { WhiteFraction = 0.2 }, false).Action);
    }

    [Fact]
    public void New_white_damage_can_request_bandage_during_potion_confirmation_without_erasing_the_potion_attempt()
    {
        var now = DateTimeOffset.UnixEpoch;
        var low = new VisualRecoveryObservation(true, 0.4, 198, VisualFoodState.Active, null, 0.1);
        var schedule = new VisualRecoverySchedule(Profile());
        schedule.RecordAttempt(VisualRecoveryAction.Potion, now, low);
        var white = low with { WhiteFraction = 0.25 };
        Assert.Equal(VisualRecoveryAction.Bandage, schedule.Decide(now.AddSeconds(1), white, false).Action);
        schedule.RecordAttempt(VisualRecoveryAction.Bandage, now.AddSeconds(1), white);
        Assert.Equal(VisualRecoveryAction.Potion, schedule.State.Pending);
        Assert.Equal(now, schedule.State.LastPotion);
        Assert.Equal(VisualRecoveryAction.None, schedule.Decide(now.AddSeconds(2), low with { HealthFraction = 0.6 }, false).Action);
        Assert.False(schedule.State.BandagePending);
        Assert.Equal(VisualRecoveryAction.None, schedule.State.Pending);
    }

    [Fact]
    public void Bandage_threshold_crossing_survives_the_next_frame_until_one_dispatch()
    {
        var now = DateTimeOffset.UnixEpoch;
        var schedule = new VisualRecoverySchedule(Profile());
        var trigger = new VisualRecoveryObservation(true, 0.353535, 198, VisualFoodState.Active, null, 0.217171);
        schedule.RecordAttempt(VisualRecoveryAction.Potion, now, trigger with { HealthFraction = 0.414141 });
        Assert.Equal(VisualRecoveryAction.Bandage, schedule.Decide(now.AddMilliseconds(800), trigger, false).Action);
        var next = trigger with { HealthFraction = 0.363636, WhiteFraction = 0.050505 };
        Assert.Equal(VisualRecoveryAction.Bandage, schedule.Decide(now.AddMilliseconds(900), next, false).Action);
        schedule.RecordAttempt(VisualRecoveryAction.Bandage, now.AddMilliseconds(900), next);
        Assert.Equal(0.050505, schedule.State.BeforeBandageWhiteFraction);
        Assert.Equal(VisualRecoveryAction.Wait, schedule.Decide(now.AddSeconds(1), next, false).Action);
        Assert.Equal(VisualRecoveryAction.None, schedule.Decide(now.AddSeconds(2), next with { WhiteFraction = 0, HealthFraction = 0.6 }, false).Action);
    }

    [Theory]
    [InlineData(true, true, 0.4)]
    [InlineData(false, false, 0.4)]
    [InlineData(false, true, 0)]
    public void Bandage_request_is_cancelled_by_stop_image_hidden_hud_or_death(bool inhibited, bool hudVisible, double health)
    {
        var now = DateTimeOffset.UnixEpoch;
        var schedule = new VisualRecoverySchedule(Profile());
        var trigger = new VisualRecoveryObservation(true, 0.4, 198, VisualFoodState.Active, null, 0.25);
        Assert.Equal(VisualRecoveryAction.Bandage, schedule.Decide(now, trigger, false).Action);
        Assert.NotEqual(VisualRecoveryAction.Bandage,
            schedule.Decide(now.AddMilliseconds(100), trigger with { HudVisible = hudVisible, HealthFraction = health }, inhibited).Action);
        Assert.Equal(VisualRecoveryAction.None,
            schedule.Decide(now.AddMilliseconds(200), trigger with { HealthFraction = 0.8, WhiteFraction = 0 }, false).Action);
    }

    [Fact]
    public void Potion_uses_threshold_and_persistent_cooldown_while_health_keeps_falling()
    {
        var now = DateTimeOffset.UnixEpoch;
        var low = new VisualRecoveryObservation(true, 0.7, 198, VisualFoodState.Active, null);
        var schedule = new VisualRecoverySchedule(Profile());
        Assert.Equal(VisualRecoveryAction.None, schedule.Decide(now, low with { HealthFraction = 0.71 }, false).Action);
        Assert.Equal(VisualRecoveryAction.Potion, schedule.Decide(now, low, false).Action);
        schedule.RecordAttempt(VisualRecoveryAction.Potion, now, low);
        var restarted = new VisualRecoverySchedule(Profile(), schedule.State);
        Assert.Equal(VisualRecoveryAction.None, restarted.Decide(now.AddSeconds(1), low, false).Action);
        Assert.Equal(VisualRecoveryAction.None, restarted.Decide(now.AddSeconds(3), low with { HealthFraction = 0.13 }, false).Action);
        Assert.Equal(VisualRecoveryAction.None, restarted.Decide(now.AddSeconds(4), low with { HealthFraction = 0.6 }, false).Action);
        Assert.Equal(VisualRecoveryAction.None, restarted.Decide(now.AddSeconds(9), low, false).Action);
        Assert.Equal(VisualRecoveryAction.Potion, restarted.Decide(now.AddSeconds(10), low, false).Action);
    }

    [Fact]
    public void Recorded_potion_health_decrease_keeps_monitoring_and_allows_next_cooldown_use()
    {
        // 実戦のF1前48.5％→3.224秒後13.1％。被弾中の差分を使用失敗にしない。
        var now = DateTimeOffset.UnixEpoch;
        var before = new VisualRecoveryObservation(true, 0.48484848484848486, 198, VisualFoodState.Active, null, 0.07575757575757576);
        var schedule = new VisualRecoverySchedule(Profile());
        schedule.RecordAttempt(VisualRecoveryAction.Potion, now, before);
        var after = before with { HealthFraction = 0.13131313131313133, WhiteFraction = 0.015151515151515152 };
        Assert.Equal(VisualRecoveryAction.None, schedule.Decide(now.AddMilliseconds(3224), after, false).Action);
        Assert.Equal(VisualRecoveryAction.None, schedule.Decide(now.AddMilliseconds(9999), after, false).Action);
        var restarted = new VisualRecoverySchedule(Profile(), schedule.State);
        Assert.Equal(VisualRecoveryAction.Potion, restarted.Decide(now.AddSeconds(10), after, false).Action);
        Assert.Equal(VisualRecoveryAction.Review, restarted.Decide(now.AddSeconds(10), after with { HealthFraction = 0 }, false).Action);
        Assert.Equal(VisualRecoveryAction.None, restarted.Decide(now.AddSeconds(10), after, true).Action);
        Assert.Equal(VisualRecoveryAction.None, restarted.Decide(now.AddSeconds(10), after with { HudVisible = false }, false).Action);
    }

    [Fact]
    public void Old_pending_potion_state_does_not_block_bandage_or_new_potion_when_health_never_rises()
    {
        var now = DateTimeOffset.UnixEpoch;
        var old = new VisualRecoveryState(LastPotion: now, Pending: VisualRecoveryAction.Potion, BeforePotion: 0.48);
        var schedule = new VisualRecoverySchedule(Profile(), old);
        var low = new VisualRecoveryObservation(true, 0.13, 198, VisualFoodState.Unknown, null, 0.25);
        Assert.Equal(VisualRecoveryAction.Bandage, schedule.Decide(now.AddSeconds(4), low, false).Action);
        schedule.RecordAttempt(VisualRecoveryAction.Bandage, now.AddSeconds(4), low);
        var healed = low with { WhiteFraction = 0.05 };
        Assert.Equal(VisualRecoveryAction.None, schedule.Decide(now.AddSeconds(5), healed, false).Action);
        Assert.Equal(VisualRecoveryAction.Potion, schedule.Decide(now.AddSeconds(10), healed, false).Action);
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
    public void クリア説明に行動不能が含まれても復活操作がなければ死亡扱いしない()
    {
        var recognizer = new VisualRecoveryRecognizer(Profile());
        Assert.False(recognizer.HasIncapacitatedDisplay("スムーズにダンジョンをクリア 行動不能にならずにクリアしました。"));
        Assert.False(recognizer.HasIncapacitatedDisplay("キャンプファイアで復活"));
        Assert.True(recognizer.HasIncapacitatedDisplay("行 動 不 能 キャンプファイアで復活"));
    }

    [Fact]
    public void Recorded_defeat_text_ends_recovery_even_when_the_health_hud_is_hidden()
    {
        var frame = Frame("../../../evidence/mabinogi-key-assist-20261008/potion-monitor-defeat.png");
        var recognizer = new VisualRecoveryRecognizer(Profile());
        var viewport = new FrameRect(1, 31, 1506, 814);
        var hidden = recognizer.Observe(frame, viewport);
        Assert.False(hidden.HudVisible);
        Assert.Null(hidden.Problem);
        // 実画面OCRで空白が挿入されても、設定した行動不能表示を識別する。
        var defeated = recognizer.Observe(frame, viewport, "サキュバスに倒されました。行 動 不 能 キャンプファイアで復活");
        var schedule = new VisualRecoverySchedule(Profile());
        Assert.Equal(VisualRecoveryAction.Review, schedule.Decide(DateTimeOffset.UnixEpoch, defeated, false).Action);
        Assert.Contains("行動不能", defeated.Problem);
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

    [Fact]
    public void 食事結果待ちや効果未確認でもポーション判定を続け食事だけ保留する()
    {
        var now = DateTimeOffset.UnixEpoch;
        var schedule = new VisualRecoverySchedule(Profile());
        var ready = new VisualRecoveryObservation(true, 1, 132, VisualFoodState.Ready, null);
        schedule.RecordAttempt(VisualRecoveryAction.Food, now, ready);
        var low = ready with { HealthFraction = 0.5 };
        Assert.Equal(VisualRecoveryAction.Potion, schedule.Decide(now.AddSeconds(1), low, false, true).Action);
        schedule.RecordAttempt(VisualRecoveryAction.Potion, now.AddSeconds(1), low);
        Assert.Equal(VisualRecoveryAction.Food, schedule.State.Pending);
        Assert.Equal(VisualRecoveryAction.Review, schedule.Decide(now.AddSeconds(8), low, false, true).Action);
        schedule.ContinueAfterReview();
        var restored = new VisualRecoverySchedule(Profile(), schedule.State);
        Assert.True(restored.State.FoodUnverified);
        Assert.Equal(VisualRecoveryAction.None, restored.Decide(now.AddSeconds(9), low, false, true).Action);
        Assert.Equal(VisualRecoveryAction.Potion, restored.Decide(now.AddSeconds(11), low, false, true).Action);
        Assert.Equal(VisualRecoveryAction.None, restored.Decide(now.AddHours(1), ready, false, true).Action);
        _ = restored.Decide(now.AddHours(1), ready with { Food = VisualFoodState.Active, BarWidth = 198 }, false, true);
        Assert.False(restored.State.FoodUnverified);
    }

    [Fact]
    public void 包帯結果待ちや未確認でもポーションを止めず包帯だけ保留する()
    {
        var now = DateTimeOffset.UnixEpoch;
        var schedule = new VisualRecoverySchedule(Profile());
        var low = new VisualRecoveryObservation(true, 0.5, 198, VisualFoodState.Active, null, 0.25);
        schedule.RecordAttempt(VisualRecoveryAction.Bandage, now, low);
        Assert.Equal(VisualRecoveryAction.Potion, schedule.Decide(now.AddSeconds(1), low, false, true).Action);
        Assert.Equal(VisualRecoveryAction.Review, schedule.Decide(now.AddSeconds(3), low, false, true).Action);
        schedule.ContinueAfterReview();
        var restored = new VisualRecoverySchedule(Profile(), schedule.State);
        Assert.True(restored.State.BandageUnverified);
        Assert.Equal(VisualRecoveryAction.Potion, restored.Decide(now.AddSeconds(6), low, false, true).Action);
        _ = restored.Decide(now.AddSeconds(7), low with { WhiteFraction = 0.1 }, false, true);
        Assert.False(restored.State.BandageUnverified);
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
