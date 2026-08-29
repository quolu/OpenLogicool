using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Host;

namespace OpenLogicool.Probe;

/// <summary>demonstration-recorder-smoke の判定1件。</summary>
internal sealed record CheckResult(string Name, bool Passed, string Detail);

/// <summary>
/// demonstration-recorder-smoke の判定。
///
/// 判定は「観測列」と「self-windowのclient frame」だけから決まる純関数である。
/// live実行から切り離してあるので、probe-output に保存済みの観測へそのまま再適用でき、
/// 判定を直した時に実OS入力を起こし直さなくても、その判定が保存済み観測に対して
/// 成立するかどうかを機械で確認できる。
/// </summary>
internal static class DemonstrationRecorderSmokeJudgement
{
    public static IReadOnlyList<CheckResult> Evaluate(
        GameCaptureScreenBounds clientBounds,
        IReadOnlyList<DemonstrationInputEdge> observed)
    {
        ArgumentNullException.ThrowIfNull(clientBounds);
        ArgumentNullException.ThrowIfNull(observed);

        var mapper = new WindowsGameInteractionCoordinateMapper(() => clientBounds);
        var centre = (
            X: clientBounds.Left + (clientBounds.Width / 2),
            Y: clientBounds.Top + (clientBounds.Height / 2));

        var pointerDowns = observed.Where(edge => edge.Kind == DemonstrationInputEdgeKind.PointerDown).ToArray();
        var pointerUps = observed.Where(edge => edge.Kind == DemonstrationInputEdgeKind.PointerUp).ToArray();
        var keyDowns = observed.Where(edge => edge.Kind == DemonstrationInputEdgeKind.KeyDown).ToArray();
        var keyUps = observed.Where(edge => edge.Kind == DemonstrationInputEdgeKind.KeyUp).ToArray();
        var wheels = observed.Where(edge => edge.Kind == DemonstrationInputEdgeKind.Wheel).ToArray();

        var checks = new List<CheckResult>
        {
            Check("pointer down を2回観測", pointerDowns.Length >= 2, $"count={pointerDowns.Length}"),
            Check("pointer up を2回観測", pointerUps.Length >= 2, $"count={pointerUps.Length}"),
            Check("key down を観測", keyDowns.Length >= 1, $"count={keyDowns.Length}"),
            Check("key up を観測", keyUps.Length >= 1, $"count={keyUps.Length}"),
            Check("wheel を観測", wheels.Length >= 1, $"count={wheels.Length}"),
            // low-level hookはdesktop全体の入力を拾うので、probeが起こしたedgeが
            // 先頭とは限らない。観測列の中に在ることで判定する。
            Check(
                "送出した Key:Esc を観測列の中に含む",
                keyDowns.Any(edge => edge.OutputToken == "Key:Esc")
                && keyUps.Any(edge => edge.OutputToken == "Key:Esc"),
                string.Join(",", keyDowns.Select(edge => edge.OutputToken).DefaultIfEmpty("(なし)"))),
            Check(
                "送出した Mouse:Left を観測列の中に含む",
                pointerDowns.Any(edge => edge.OutputToken == "Mouse:Left")
                && pointerUps.Any(edge => edge.OutputToken == "Mouse:Left"),
                string.Join(",", pointerDowns.Select(edge => edge.OutputToken).DefaultIfEmpty("(なし)"))),
            Check(
                "key edge に pointer 座標が付かない",
                keyDowns.All(edge => edge.ScreenPoint is null) && keyUps.All(edge => edge.ScreenPoint is null),
                "ScreenPoint=null"),
            Check(
                "wheel の段数が 0 でない",
                wheels.Length >= 1 && (wheels[0].WheelVerticalSteps != 0 || wheels[0].WheelHorizontalSteps != 0),
                wheels.Length >= 1 ? $"v={wheels[0].WheelVerticalSteps} h={wheels[0].WheelHorizontalSteps}" : "(なし)"),
        };

        var normalizedCentre = mapper.TryMapScreenToNormalized(centre.X, centre.Y);
        var normalizedOutside = mapper.TryMapScreenToNormalized(clientBounds.Left - 40, clientBounds.Top - 40);
        checks.Add(Check(
            "client frame 中央が 0.5 付近へ正規化される",
            normalizedCentre is not null
            && Math.Abs(normalizedCentre[0] - 0.5) <= 0.01
            && Math.Abs(normalizedCentre[1] - 0.5) <= 0.01,
            normalizedCentre is null ? "null" : $"[{normalizedCentre[0]:F4}, {normalizedCentre[1]:F4}]"));
        checks.Add(Check(
            "client frame 外は null（desktop 絶対座標を保存しない）",
            normalizedOutside is null,
            normalizedOutside is null ? "null" : "正規化された"));

        var observedInsideFrame = pointerDowns
            .Select(edge => edge.ScreenPoint is null
                ? null
                : mapper.TryMapScreenToNormalized(edge.ScreenPoint.X, edge.ScreenPoint.Y))
            .ToArray();
        checks.Add(Check(
            "観測した pointer down はすべて client frame 内で正規化できる",
            observedInsideFrame.Length >= 2 && observedInsideFrame.All(point => point is not null),
            string.Join(
                " / ",
                observedInsideFrame.Select(point => point is null ? "null" : $"[{point[0]:F4}, {point[1]:F4}]"))));

        return checks;
    }

    private static CheckResult Check(string name, bool passed, string detail) => new(name, passed, detail);
}
