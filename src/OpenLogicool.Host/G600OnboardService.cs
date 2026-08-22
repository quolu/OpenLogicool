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

        if (!TryEnsureActiveSlot0(out var slotError))
        {
            return new(false, slotError!);
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

        // 巻き戻り検出（実測 2026-08-22: verify 一致後に本体側で内容が戻るケースがあった）。
        var confirm = G600EvidenceWrite.TryRead(_access, G600EvidenceWrite.ProfileReportIdF3, _sleep, _settleMs);
        if (confirm is null || !confirm.SequenceEqual(payload))
        {
            return new(false, "書き込みは一度成立しましたが、再読込で内容が巻き戻っていました。LGS 等の常駐や本体の状態を確認してください。");
        }

        _mode.Save(new G600OnboardModeState(workspaceId, g600Document.ProfileId, DateTimeOffset.UtcNow));
        return new(true, $"G600 本体へ書き込みました（attempt {write.Attempts}・byte 一致・再確認済み）。");
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

        if (!TryEnsureActiveSlot0(out var slotError))
        {
            return new(false, slotError!);
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

    /// <summary>現在の active slot（読めなければ null）。status 表示用。</summary>
    public int? ReadActiveSlot()
    {
        var f0 = G600EvidenceWrite.TryRead(_access, G600ActiveSlot.ReportId, _sleep, settleMs: 0);
        return f0 is null ? null : G600ActiveSlot.ReadIndex(f0[1]);
    }

    /// <summary>
    /// 書込み対象の F3 が生きる条件＝active slot 0 を強制する（EXP-G600-03 実証の切替）。
    /// slot 0 以外で F3 へ書くと verify 一致後に巻き戻る実測があるため（2026-08-22・LGS が slot 2 へ変更）、
    /// 切替が成立しない場合は書込みへ進まない。
    /// </summary>
    private bool TryEnsureActiveSlot0(out string? error)
    {
        error = null;
        var f0 = G600EvidenceWrite.TryRead(_access, G600ActiveSlot.ReportId, _sleep, _settleMs);
        if (f0 is null)
        {
            error = "G600 の状態（F0）を読めませんでした。";
            return false;
        }

        if (G600ActiveSlot.ReadIndex(f0[1]) == 0)
        {
            return true;
        }

        var payload = G600ActiveSlot.BuildSwitch(0);
        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            if (!_access.TryOpen(out var writeHandle) || writeHandle is null)
            {
                error = $"本体の使用面切替のために G600 を開けませんでした（attempt {attempt}）。";
                return false;
            }

            using (writeHandle)
            {
                _sleep(_settleMs);
                writeHandle.SetFeature(payload);
            }

            var verify = G600EvidenceWrite.TryRead(_access, G600ActiveSlot.ReportId, _sleep, _settleMs);
            if (verify is not null && G600ActiveSlot.ReadIndex(verify[1]) == 0)
            {
                return true;
            }
        }

        error = $"本体の使用面を書込み対象へ切り替えられませんでした（{_maxAttempts} 回試行）。";
        return false;
    }
}
