using System.IO;
using System.Text.Json;
using OpenLogicool.Contracts.Capture;

namespace OpenLogicool.Host;

public enum VisualFoodState { Ready, Active, Unknown }
public enum VisualRecoveryAction { None, Wait, Food, Potion, Review, Bandage }

public sealed record VisualRecoveryObservation(bool HudVisible, double? HealthFraction,
    int? BarWidth, VisualFoodState Food, string? Problem, double? WhiteFraction = null);

/// <summary>実画面で校正したHUDと使用キー。ゲーム固有の画像と座標はデータに置く。</summary>
public sealed record VisualRecoveryProfile(
    int SchemaVersion, int Width, int Height,
    string HudImage, double[] HudSearch,
    string FoodReadyImage, double[] FoodReadySearch,
    string FoodActiveImage, double[] FoodActiveSearch,
    int[] RailSearch, int[] RailRgb, int RailTolerance, int CapPixels, int FillOffsetY,
    int MinimumBarWidth, string FoodKey, string PotionKey,
    double PotionThreshold, int PotionCooldownMs, int FoodMinimumIntervalMs,
    int[] Viewport, double CanvasAspectRatio,
    string BandageKey, double BandageThreshold, int BandageCooldownMs,
    string? IncapacitatedText = null, string? IncapacitatedContextText = null)
{
    public static VisualRecoveryProfile Load(string path)
    {
        var profile = JsonSerializer.Deserialize<VisualRecoveryProfile>(File.ReadAllText(path))
            ?? throw new InvalidDataException("回復判定設定が空です。");
        if (profile.SchemaVersion != 1 || profile.Width <= 0 || profile.Height <= 0
            || profile.RailSearch is not { Length: 4 } || profile.RailRgb is not { Length: 3 }
            || profile.RailSearch[0] < 0 || profile.RailSearch[1] + profile.FillOffsetY < 0
            || profile.RailSearch[2] <= 0 || profile.RailSearch[3] <= 0
            || profile.RailSearch[0] + profile.RailSearch[2] > profile.Width
            || profile.RailSearch[1] + profile.RailSearch[3] > profile.Height
            || profile.RailRgb.Any(value => value is < 0 or > 255)
            || profile.RailTolerance is < 0 or > 255 || profile.CapPixels < 0
            || profile.MinimumBarWidth <= 0 || profile.PotionThreshold is <= 0 or > 1
            || profile.PotionCooldownMs <= 0 || profile.FoodMinimumIntervalMs <= 0
            || string.IsNullOrWhiteSpace(profile.BandageKey)
            || !double.IsFinite(profile.BandageThreshold) || profile.BandageThreshold is <= 0 or > 1
            || profile.BandageCooldownMs <= 0)
            throw new InvalidDataException("回復判定設定の形式または値が不正です。");
        if (profile.Viewport is not { Length: 4 } || profile.Viewport.Any(value => value < 0)
            || profile.Viewport[2] == 0 || profile.Viewport[3] == 0
            || profile.Viewport[0] + profile.Viewport[2] > profile.Width
            || profile.Viewport[1] + profile.Viewport[3] > profile.Height
            || !double.IsFinite(profile.CanvasAspectRatio) || profile.CanvasAspectRatio <= 0)
            throw new InvalidDataException("回復判定の描画領域設定が不正です。");
        foreach (var bounds in new[] { profile.HudSearch, profile.FoodReadySearch, profile.FoodActiveSearch })
            if (bounds is not { Length: 4 } || bounds.Any(value => !double.IsFinite(value) || value < 0 || value > 1)
                || bounds[2] <= 0 || bounds[3] <= 0 || bounds[0] + bounds[2] > 1 || bounds[1] + bounds[3] > 1)
                throw new InvalidDataException("回復判定の画像探索範囲が不正です。");
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        return profile with
        {
            HudImage = Path.GetFullPath(profile.HudImage, directory),
            FoodReadyImage = Path.GetFullPath(profile.FoodReadyImage, directory),
            FoodActiveImage = Path.GetFullPath(profile.FoodActiveImage, directory),
        };
    }
}

public sealed class VisualRecoveryRecognizer(VisualRecoveryProfile profile)
{
    private readonly VisualKeyTemplate hud = VisualKeyTemplate.Load(profile.HudImage);
    private readonly VisualKeyTemplate ready = VisualKeyTemplate.Load(profile.FoodReadyImage);
    private readonly VisualKeyTemplate active = VisualKeyTemplate.Load(profile.FoodActiveImage, relativeColor: true);

    public bool HasIncapacitatedDisplay(string screenText) =>
        !string.IsNullOrWhiteSpace(profile.IncapacitatedText)
        && VisualKeyAssistRuntime.ContainsCue(screenText, profile.IncapacitatedText)
        && (string.IsNullOrWhiteSpace(profile.IncapacitatedContextText)
            || VisualKeyAssistRuntime.ContainsCue(screenText, profile.IncapacitatedContextText));

    public VisualRecoveryObservation Observe(CapturedFrame frame, FrameRect? viewport = null, string? screenText = null)
    {
        if (screenText is not null && HasIncapacitatedDisplay(screenText))
            return new(false, null, null, VisualFoodState.Unknown, "対象の行動不能表示を確認しました。回復入力を終了します。");
        var sourceFrame = frame;
        if (viewport is null && (frame.Width != profile.Width || frame.Height != profile.Height))
            return new(false, null, null, VisualFoodState.Unknown, "ゲームの描画領域が必要です。");
        if (viewport is not null)
        {
            if (viewport.X < 0 || viewport.Y < 0 || viewport.Width <= 0 || viewport.Height <= 0
                || viewport.X + viewport.Width > frame.Width || viewport.Y + viewport.Height > frame.Height)
                return new(true, null, null, VisualFoodState.Unknown, "撮影中に描画領域が変わりました。");
            frame = NormalizeHud(frame, viewport);
        }
        if (!hud.Find(frame, profile.HudSearch).Matches)
            return new(false, null, null, VisualFoodState.Unknown, null);
        var foodReady = viewport is null || sourceFrame == frame
            ? ready.FindNativeSize(frame, profile.FoodReadySearch).Matches
            : ready.FindAtScale(sourceFrame, MapSearch(sourceFrame, viewport, profile.FoodReadySearch), HudScale(viewport)).Matches;
        var foodActive = active.Find(frame, profile.FoodActiveSearch).Difference <= 8;
        var food = (foodReady, foodActive) switch
        {
            (true, false) => VisualFoodState.Ready,
            (false, true) => VisualFoodState.Active,
            _ => VisualFoodState.Unknown,
        };
        var pixels = frame.Pixels ?? throw new InvalidOperationException("回復判定の画像がありません。");
        var bytes = pixels.Bgra8.Span;
        var area = profile.RailSearch;
        // 被弾の白い表示は下辺を分断する。左端と丸い右端を独立に探す。
        var bestLeft = -1;
        var bestRight = -1;
        var bestY = -1;
        foreach (var y in Enumerable.Range(area[1], area[3]).OrderBy(y => Math.Abs(y - (area[1] + area[3] / 2))))
        {
            var start = -1;
            for (var x = area[0]; x < area[0] + 15; x++)
                if (IsRail(bytes, pixels.Stride, x, y)) { start = x; break; }
            if (start < 0) continue;
            for (var x = start + profile.MinimumBarWidth - 1; x < area[0] + area[2]; x++)
            {
                if (IsRail(bytes, pixels.Stride, x, y)
                    && x - start > bestRight - bestLeft
                    // 縮小画像を基準座標へ戻すと下辺の高さが1行ずれる。隣接する下側の行で丸い端を確かめる。
                    && y + 2 < frame.Height
                    && ((IsRail(bytes, pixels.Stride, x - 4, y + 1) && !IsRail(bytes, pixels.Stride, x, y + 1))
                        || (IsRail(bytes, pixels.Stride, x - 4, y + 2) && !IsRail(bytes, pixels.Stride, x, y + 2)))
                    && !IsRail(bytes, pixels.Stride, x + 1, y))
                {
                    bestLeft = start;
                    bestRight = x;
                    bestY = y;
                }
            }
            if (bestLeft >= 0) break;
        }
        if (bestLeft < 0)
            return new(true, null, null, food, "HPバーの枠を識別できません。");
        var left = bestLeft - profile.CapPixels;
        var right = bestRight + profile.CapPixels;
        var fillY = bestY + profile.FillOffsetY;
        if (left < 0 || right >= frame.Width || fillY < 0 || fillY >= frame.Height)
            return new(true, null, null, food, "HPバーの測定範囲が画面外です。");
        var filledRight = left - 1;
        var whitePixels = 0;
        for (var x = left; x <= right; x++)
        {
            var offset = fillY * pixels.Stride + x * 4;
            var maximum = Math.Max(bytes[offset], Math.Max(bytes[offset + 1], bytes[offset + 2]));
            var minimum = Math.Min(bytes[offset], Math.Min(bytes[offset + 1], bytes[offset + 2]));
            // 水色の追加HPも充填として測る。目盛りの暗い縦線は残量の終端にしない。
            if (maximum >= 110 && maximum - minimum >= 45) filledRight = x;
            // 白い部分は色付きHPと別に測り、目盛りで分割されても全長に対する割合を返す。
            if (minimum >= 170 && maximum - minimum <= 25) whitePixels++;
        }
        var width = right - left + 1;
        return new(true, (filledRight - left + 1) / (double)width, width, food, null, whitePixels / (double)width);
    }

    private CapturedFrame NormalizeHud(CapturedFrame frame, FrameRect viewport)
    {
        var reference = profile.Viewport;
        if (frame.Width == profile.Width && frame.Height == profile.Height
            && viewport == new FrameRect(reference[0], reference[1], reference[2], reference[3])) return frame;
        var scale = HudScale(viewport);
        var source = frame.Pixels ?? throw new InvalidOperationException("回復判定の画像がありません。");
        var bytes = source.Bgra8.Span;
        var normalized = new byte[profile.Width * profile.Height * 4];
        // 窓枠を除いた描画領域の倍率で、判定に使う左上のHUDだけを基準座標へ戻す。
        var right = Math.Min(profile.Width, Math.Max(profile.RailSearch[0] + profile.RailSearch[2],
            (int)Math.Ceiling((profile.FoodReadySearch[0] + profile.FoodReadySearch[2]) * profile.Width)) + 2);
        var bottom = Math.Min(profile.Height,
            (int)Math.Ceiling((profile.FoodActiveSearch[1] + profile.FoodActiveSearch[3]) * profile.Height) + 2);
        for (var y = reference[1]; y < bottom; y++)
        for (var x = reference[0]; x < right; x++)
        {
            var sourceX = (int)Math.Round(viewport.X + (x - reference[0]) * scale);
            var sourceY = (int)Math.Round(viewport.Y + (y - reference[1]) * scale);
            if (sourceX >= viewport.X + viewport.Width || sourceY >= viewport.Y + viewport.Height) continue;
            bytes.Slice(sourceY * source.Stride + sourceX * 4, 4).CopyTo(normalized.AsSpan((y * profile.Width + x) * 4, 4));
        }
        return frame with { Width = profile.Width, Height = profile.Height,
            Pixels = new FramePixels(normalized, profile.Width * 4) };
    }

    public double HudScale(FrameRect viewport) => Math.Min(viewport.Width, viewport.Height * profile.CanvasAspectRatio)
        / Math.Min(profile.Viewport[2], profile.Viewport[3] * profile.CanvasAspectRatio);

    private double[] MapSearch(CapturedFrame frame, FrameRect viewport, double[] search)
    {
        var scale = HudScale(viewport);
        return [
            (viewport.X + (search[0] * profile.Width - profile.Viewport[0]) * scale) / frame.Width,
            (viewport.Y + (search[1] * profile.Height - profile.Viewport[1]) * scale) / frame.Height,
            search[2] * profile.Width * scale / frame.Width,
            search[3] * profile.Height * scale / frame.Height,
        ];
    }

    private bool IsRail(ReadOnlySpan<byte> bytes, int stride, int x, int y)
    {
        var offset = y * stride + x * 4;
        return Math.Abs(bytes[offset + 2] - profile.RailRgb[0]) <= profile.RailTolerance
            && Math.Abs(bytes[offset + 1] - profile.RailRgb[1]) <= profile.RailTolerance
            && Math.Abs(bytes[offset] - profile.RailRgb[2]) <= profile.RailTolerance;
    }
}

public sealed record VisualRecoveryState(DateTimeOffset? LastFood = null, DateTimeOffset? LastPotion = null,
    VisualRecoveryAction Pending = VisualRecoveryAction.None, double? BeforePotion = null,
    int? BeforeFoodBarWidth = null, DateTimeOffset? LastBandage = null,
    bool BandagePending = false, double? BeforeBandageWhiteFraction = null);
public sealed record VisualRecoveryChoice(VisualRecoveryAction Action, string Detail);

/// <summary>食事の結果待ちと回復品の待ち時間を保持する。被弾中のHP差分でポーション使用の成否を判定しない。</summary>
public sealed class VisualRecoverySchedule(VisualRecoveryProfile profile, VisualRecoveryState? initial = null)
{
    public VisualRecoveryState State { get; private set; } = initial ?? new();
    private DateTimeOffset? uncertainHealthSince;
    private bool bandageRequested;

    public VisualRecoveryChoice Decide(DateTimeOffset now, VisualRecoveryObservation observation, bool inhibited)
    {
        if (inhibited || !observation.HudVisible || observation.HealthFraction == 0 || observation.Problem is not null)
            bandageRequested = false;
        if (inhibited) return new(VisualRecoveryAction.None, "停止画像を優先します。");
        if (observation.Problem is not null)
        {
            if (!observation.HudVisible) return new(VisualRecoveryAction.Review, observation.Problem);
            uncertainHealthSince ??= now;
            return now - uncertainHealthSince < TimeSpan.FromSeconds(2)
                ? new(VisualRecoveryAction.Wait, "HP表示を識別できません。入力せず表示の安定を観測しています。")
                : new(VisualRecoveryAction.Review, observation.Problem);
        }
        uncertainHealthSince = null;
        if (State.BandagePending)
        {
            if (observation.HudVisible && observation.WhiteFraction < State.BeforeBandageWhiteFraction)
                State = State with { BandagePending = false };
            else return now - State.LastBandage >= TimeSpan.FromSeconds(3)
                ? new(VisualRecoveryAction.Review, "包帯後に白い部分の減少を確認できません。追加使用せず停止しました。")
                : new(VisualRecoveryAction.Wait, "包帯後の白い部分を確認しています。");
        }
        // 閾値への到達は1回の使用要求。送出直前の別frameで白が減っても要求を失わない。
        if (observation.HudVisible && observation.HealthFraction > 0
            && (State.LastBandage is null || now - State.LastBandage >= TimeSpan.FromMilliseconds(profile.BandageCooldownMs))
            && observation.WhiteFraction >= profile.BandageThreshold)
            bandageRequested = true;
        if (bandageRequested)
            return new(VisualRecoveryAction.Bandage, "HPバーの白い部分が包帯の使用基準以上です。");
        if (State.Pending == VisualRecoveryAction.Food)
        {
            if (observation.HudVisible && observation.Food == VisualFoodState.Active
                && observation.BarWidth > State.BeforeFoodBarWidth)
                State = State with { Pending = VisualRecoveryAction.None };
            else return now - State.LastFood >= TimeSpan.FromSeconds(8)
                ? new(VisualRecoveryAction.Review, "食事後の効果を確認できません。再使用せず停止しました。")
                : new(VisualRecoveryAction.Wait, "食事効果を確認しています。");
        }
        if (State.Pending == VisualRecoveryAction.Potion)
        {
            // HPの差分は被ダメージも含む。増加が見えなくても監視と次回の使用を止めない。
            if (observation.HealthFraction > State.BeforePotion
                || now - State.LastPotion >= TimeSpan.FromMilliseconds(profile.PotionCooldownMs))
                State = State with { Pending = VisualRecoveryAction.None };
        }
        if (!observation.HudVisible) return new(VisualRecoveryAction.None, "HUD非表示中は消費しません。");
        if (observation.HealthFraction == 0)
            return new(VisualRecoveryAction.Review, "HPの充填部分がありません。行動不能かどうかの確認が必要です。");
        if (observation.HealthFraction <= profile.PotionThreshold)
            return State.LastPotion is null || now - State.LastPotion >= TimeSpan.FromMilliseconds(profile.PotionCooldownMs)
                ? new(VisualRecoveryAction.Potion, "HPの割合が回復基準以下です。")
                : new(VisualRecoveryAction.None, "ポーションの待ち時間中です。HPと包帯の監視は継続します。");
        if (observation.Food == VisualFoodState.Unknown)
            return new(VisualRecoveryAction.None, "食事は未判別のため使用しません。HP監視は続けます。");
        if (observation.Food == VisualFoodState.Ready)
            return State.LastFood is null || now - State.LastFood >= TimeSpan.FromMilliseconds(profile.FoodMinimumIntervalMs)
                ? new(VisualRecoveryAction.Food, "食事ボタンがあり、食事効果がありません。")
                : new(VisualRecoveryAction.None, "食事の再使用間隔内です。追加消費せずHPと包帯の監視を続けます。");
        return new(VisualRecoveryAction.None, "回復・食事の使用は不要です。");
    }

    public void RecordAttempt(VisualRecoveryAction action, DateTimeOffset now, VisualRecoveryObservation observation)
    {
        State = action switch
        {
            VisualRecoveryAction.Food => State with { LastFood = now, Pending = action, BeforeFoodBarWidth = observation.BarWidth },
            VisualRecoveryAction.Potion => State with { LastPotion = now, Pending = action, BeforePotion = observation.HealthFraction },
            VisualRecoveryAction.Bandage => State with { LastBandage = now, BandagePending = true,
                BeforeBandageWhiteFraction = observation.WhiteFraction },
            _ => throw new ArgumentException("消費操作だけを記録できます。", nameof(action)),
        };
        if (action == VisualRecoveryAction.Bandage) bandageRequested = false;
    }
}
