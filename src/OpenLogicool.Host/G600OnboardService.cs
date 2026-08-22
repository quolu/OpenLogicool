using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Devices.G600;

namespace OpenLogicool.Host;

public sealed record G600OnboardOperationResult(bool Success, string Message);

/// <summary>
/// 方式A: workspace の G600 割当を onboard F3 へ焼く／出荷状態へ戻す。fast path の外でだけ呼ぶ。
/// write 作法は G600EvidenceWrite（fresh open・settle・handle 非再利用・fresh verify・一致まで再送）。
/// baseline（出荷状態の F3）は残置（leftover）と共有し、常に「出荷状態」を指す:
/// 未保存なら書込み前の現物を保存し、保存済み（残置が先に確保した等）ならそれを維持して payload の起点にする。
/// </summary>
public sealed class G600OnboardService
{
    private readonly IG600FeatureAccess _access;
    private readonly IG600OnboardBaselineStore _baseline;
    private readonly G600OnboardModeStore _mode;
    private readonly Func<bool> _coexistenceRunning;
    private readonly Action<int> _sleep;
    private readonly int _settleMs;
    private readonly int _maxAttempts;

    public G600OnboardService(
        IG600FeatureAccess access,
        IG600OnboardBaselineStore baseline,
        G600OnboardModeStore mode,
        Func<bool> coexistenceRunning,
        Action<int>? sleep = null,
        int settleMs = G600EvidenceWrite.DefaultSettleMs,
        int maxAttempts = G600EvidenceWrite.DefaultMaxAttempts)
    {
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
        _mode = mode ?? throw new ArgumentNullException(nameof(mode));
        _coexistenceRunning = coexistenceRunning ?? throw new ArgumentNullException(nameof(coexistenceRunning));
        _sleep = sleep ?? Thread.Sleep;
        _settleMs = settleMs;
        _maxAttempts = maxAttempts;
    }

    public static G600OnboardService CreateDefault(string databasePath)
    {
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(databasePath));
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"database path has no directory: {databasePath}");
        }

        return new G600OnboardService(
            new G600FeatureHidAccess(),
            new FileG600OnboardBaselineStore(directory),
            new G600OnboardModeStore(directory),
            G600LeftoverHostSupport.IsCoexistenceRunning);
    }

    public G600OnboardModeState? CurrentMode() => _mode.Load();

    public G600OnboardOperationResult Apply(string workspaceId, MappingProfileDocument g600Document)
    {
        if (_coexistenceRunning())
        {
            return new(false, "LGS / G HUB / Logi Options+ の実行中は G600 本体へ書き込みません。終了してから実行してください。");
        }

        var plan = G600OnboardPlanner.Build(g600Document);
        if (!plan.CanApply)
        {
            return new(false, "本体で表現できない割当があるため書き込みません:\n" + string.Join("\n", plan.Errors));
        }

        var current = G600EvidenceWrite.TryRead(_access, G600EvidenceWrite.ProfileReportIdF3, _sleep, _settleMs);
        if (current is null)
        {
            return new(false, "G600 が見つからないため書き込めません。接続を確認してください。");
        }

        var baseline = _baseline.LoadF3();
        if (baseline is null)
        {
            // 復元元を write より先に残す（apply 失敗時も戻せる）。
            _baseline.SaveF3(current);
            baseline = current;
        }

        var payload = G600OnboardImage.Build(baseline, plan.Cells, plan.ShiftSelectorButton);
        var write = G600EvidenceWrite.TryWrite(_access, payload, _sleep, _maxAttempts, _settleMs);
        if (write.OpenError is not null)
        {
            return new(false, $"G600 を開けませんでした: {write.OpenError}");
        }

        if (!write.Matched)
        {
            return new(false, $"書き込みが {write.Attempts} 回で byte 一致しませんでした。本体の状態を status で確認してください。");
        }

        _mode.Save(new G600OnboardModeState(workspaceId, g600Document.ProfileId, DateTimeOffset.UtcNow));
        return new(true, $"G600 本体へ書き込みました（attempt {write.Attempts}・byte 一致）。");
    }

    public G600OnboardOperationResult Restore()
    {
        if (_coexistenceRunning())
        {
            return new(false, "LGS / G HUB / Logi Options+ の実行中は G600 本体へ書き込みません。終了してから実行してください。");
        }

        var baseline = _baseline.LoadF3();
        if (baseline is null)
        {
            return new(false, "復元元（書込み前の記録）が見つからないため戻せません。");
        }

        var write = G600EvidenceWrite.TryWrite(_access, baseline, _sleep, _maxAttempts, _settleMs);
        if (write.OpenError is not null)
        {
            return new(false, $"G600 を開けませんでした: {write.OpenError}");
        }

        if (!write.Matched)
        {
            return new(false, $"復元が {write.Attempts} 回で byte 一致しませんでした。復元元は保持したままです。");
        }

        _mode.Clear();
        return new(true, $"G600 本体を書込み前の状態へ戻しました（attempt {write.Attempts}・byte 一致）。");
    }
}
