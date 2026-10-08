using System.IO;
using System.Text.Json;
using OpenLogicool.Contracts.Capture;

namespace OpenLogicool.Host;

public enum VisualFoodState { Ready, Active, Unknown }
public enum VisualRecoveryAction { None, Wait, Food, Potion, Review }

public sealed record VisualRecoveryObservation(bool HudVisible, double? HealthFraction,
    int? BarWidth, VisualFoodState Food, string? Problem);

/// <summary>実画面で校正したHUDと使用キー。ゲーム固有の画像と座標はデータに置く。</summary>
public sealed record VisualRecoveryProfile(
    int SchemaVersion, int Width, int Height,
    string HudImage, double[] HudSearch,
    string FoodReadyImage, double[] FoodReadySearch,
    string FoodActiveImage, double[] FoodActiveSearch,
    int[] RailSearch, int[] RailRgb, int RailTolerance, int CapPixels, int FillOffsetY,
    int MinimumBarWidth, string FoodKey, string PotionKey,
    double PotionThreshold, int PotionCooldownMs, int FoodMinimumIntervalMs)
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
            || profile.PotionCooldownMs <= 0 || profile.FoodMinimumIntervalMs <= 0)
            throw new InvalidDataException("回復判定設定の形式または値が不正です。");
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

    public VisualRecoveryObservation Observe(CapturedFrame frame)
    {
        if (frame.Width != profile.Width || frame.Height != profile.Height)
            return new(false, null, null, VisualFoodState.Unknown, "校正した画面サイズと異なります。");
        if (!hud.Find(frame, profile.HudSearch).Matches)
            return new(false, null, null, VisualFoodState.Unknown, null);
        var foodReady = ready.Find(frame, profile.FoodReadySearch).Matches;
        var foodActive = active.Find(frame, profile.FoodActiveSearch).Matches;
        var food = (foodReady, foodActive) switch
        {
            (true, false) => VisualFoodState.Ready,
            (false, true) => VisualFoodState.Active,
            _ => VisualFoodState.Unknown,
        };
        var pixels = frame.Pixels ?? throw new InvalidOperationException("回復判定の画像がありません。");
        var bytes = pixels.Bgra8.Span;
        var area = profile.RailSearch;
        // 下辺の連続した枠を探す。残量や食事で変わる右端を固定値にしない。
        var bestLeft = -1;
        var bestRight = -1;
        var bestY = -1;
        for (var y = area[1]; y < area[1] + area[3]; y++)
        {
            var start = -1;
            for (var x = area[0]; x <= area[0] + area[2]; x++)
            {
                var rail = x < area[0] + area[2] && IsRail(bytes, pixels.Stride, x, y);
                if (rail && start < 0) start = x;
                if (rail || start < 0) continue;
                if (start <= area[0] + 15 && x - start >= profile.MinimumBarWidth
                    && x - start > bestRight - bestLeft + 1
                    && y + 2 < frame.Height
                    && IsRail(bytes, pixels.Stride, (start + x - 1) / 2, y + 2)
                    && IsRail(bytes, pixels.Stride, x - 5, y + 2)
                    && !IsRail(bytes, pixels.Stride, x - 1, y + 2))
                {
                    bestLeft = start;
                    bestRight = x - 1;
                    bestY = y;
                }
                start = -1;
            }
        }
        if (bestLeft < 0)
            return new(true, null, null, food, "HPバーの枠を識別できません。");
        var left = bestLeft - profile.CapPixels;
        var right = bestRight + profile.CapPixels;
        var fillY = bestY + profile.FillOffsetY;
        if (left < 0 || right >= frame.Width || fillY < 0 || fillY >= frame.Height)
            return new(true, null, null, food, "HPバーの測定範囲が画面外です。");
        var filledRight = left - 1;
        for (var x = left; x <= right; x++)
        {
            var offset = fillY * pixels.Stride + x * 4;
            var maximum = Math.Max(bytes[offset], Math.Max(bytes[offset + 1], bytes[offset + 2]));
            var minimum = Math.Min(bytes[offset], Math.Min(bytes[offset + 1], bytes[offset + 2]));
            // 水色の追加HPも充填として測る。目盛りの暗い縦線は残量の終端にしない。
            if (maximum >= 110 && maximum - minimum >= 45) filledRight = x;
        }
        var width = right - left + 1;
        return new(true, (filledRight - left + 1) / (double)width, width, food, null);
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
    VisualRecoveryAction Pending = VisualRecoveryAction.None, double? BeforePotion = null);
public sealed record VisualRecoveryChoice(VisualRecoveryAction Action, string Detail);

/// <summary>食事の結果待ちと薬の待ち時間を保持し、未確認の消費を繰り返さない。</summary>
public sealed class VisualRecoverySchedule(VisualRecoveryProfile profile, VisualRecoveryState? initial = null)
{
    public VisualRecoveryState State { get; private set; } = initial ?? new();
    private DateTimeOffset? uncertainHealthSince;

    public VisualRecoveryChoice Decide(DateTimeOffset now, VisualRecoveryObservation observation, bool inhibited)
    {
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
        if (State.Pending == VisualRecoveryAction.Food)
        {
            if (observation.HudVisible && observation.Food == VisualFoodState.Active)
                State = State with { Pending = VisualRecoveryAction.None };
            else return now - State.LastFood >= TimeSpan.FromSeconds(8)
                ? new(VisualRecoveryAction.Review, "食事後の効果を確認できません。再使用せず停止しました。")
                : new(VisualRecoveryAction.Wait, "食事効果を確認しています。");
        }
        if (State.Pending == VisualRecoveryAction.Potion)
        {
            if (observation.HealthFraction > State.BeforePotion)
                State = State with { Pending = VisualRecoveryAction.None };
            else return now - State.LastPotion >= TimeSpan.FromSeconds(3)
                ? new(VisualRecoveryAction.Review, "ポーション後のHP増加を確認できません。追加入力を停止しました。")
                : new(VisualRecoveryAction.Wait, "ポーション後のHPを確認しています。");
        }
        if (!observation.HudVisible) return new(VisualRecoveryAction.None, "HUD非表示中は消費しません。");
        if (observation.HealthFraction == 0)
            return new(VisualRecoveryAction.Review, "HPの充填部分がありません。行動不能かどうかの確認が必要です。");
        if (observation.HealthFraction <= profile.PotionThreshold)
            return State.LastPotion is null || now - State.LastPotion >= TimeSpan.FromMilliseconds(profile.PotionCooldownMs)
                ? new(VisualRecoveryAction.Potion, "HPの割合が回復基準以下です。")
                : new(VisualRecoveryAction.None, "ポーションの待ち時間中です。");
        if (observation.Food == VisualFoodState.Unknown)
            return new(VisualRecoveryAction.None, "食事は未判別のため使用しません。HP監視は続けます。");
        if (observation.Food == VisualFoodState.Ready)
            return State.LastFood is null || now - State.LastFood >= TimeSpan.FromMilliseconds(profile.FoodMinimumIntervalMs)
                ? new(VisualRecoveryAction.Food, "食事ボタンがあり、食事効果がありません。")
                : new(VisualRecoveryAction.Review, "食事の効果時間内に使用前の表示へ戻りました。追加消費せず確認します。");
        return new(VisualRecoveryAction.None, "回復・食事の使用は不要です。");
    }

    public void RecordAttempt(VisualRecoveryAction action, DateTimeOffset now, VisualRecoveryObservation observation)
    {
        State = action switch
        {
            VisualRecoveryAction.Food => State with { LastFood = now, Pending = action },
            VisualRecoveryAction.Potion => State with { LastPotion = now, Pending = action, BeforePotion = observation.HealthFraction },
            _ => throw new ArgumentException("消費操作だけを記録できます。", nameof(action)),
        };
    }
}
