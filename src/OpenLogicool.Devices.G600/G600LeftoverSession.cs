namespace OpenLogicool.Devices.G600;

public sealed record G600LeftoverResult(
    G600LeftoverKind Kind,
    string Reason,
    bool Wrote,
    int Attempts,
    string? OpenError,
    bool ByteMatched)
{
    public bool IsHardFailure =>
        Kind == G600LeftoverKind.RefuseAppliedWithoutBaseline
        || OpenError is not null
        || (Wrote && !ByteMatched);
}

/// <summary>
/// Input Studio 管理下での B変種残置（apply）と解除（restore）。
/// fast path の外でだけ呼ぶ。profile 切替では呼ばない。
/// </summary>
public sealed class G600LeftoverSession
{
    private readonly IG600FeatureAccess _access;
    private readonly IG600OnboardBaselineStore _baseline;
    private readonly Func<bool> _coexistenceRunning;
    private readonly Action<int> _sleep;
    private readonly int _settleMs;
    private readonly int _maxAttempts;

    public G600LeftoverSession(
        IG600FeatureAccess access,
        IG600OnboardBaselineStore baseline,
        Func<bool> coexistenceRunning,
        Action<int>? sleep = null,
        int settleMs = G600EvidenceWrite.DefaultSettleMs,
        int maxAttempts = G600EvidenceWrite.DefaultMaxAttempts)
    {
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
        _coexistenceRunning = coexistenceRunning ?? throw new ArgumentNullException(nameof(coexistenceRunning));
        _sleep = sleep ?? Thread.Sleep;
        _settleMs = settleMs;
        _maxAttempts = maxAttempts;
    }

    public static G600LeftoverSession CreateDefault(string baselineDirectory, Func<bool> coexistenceRunning) =>
        new(new G600FeatureHidAccess(), new FileG600OnboardBaselineStore(baselineDirectory), coexistenceRunning);

    public G600LeftoverResult Apply(
        bool managed,
        G600LegacySuppressionMode mode = G600LegacySuppressionMode.IntermediateUsage)
    {
        var current = managed ? G600EvidenceWrite.TryRead(_access, G600EvidenceWrite.ProfileReportIdF3, _sleep, _settleMs) : null;
        var baseline = _baseline.LoadF3();
        var decision = G600LeftoverPolicy.DecideApply(
            managed,
            devicePresent: current is not null,
            _coexistenceRunning(),
            current,
            baseline,
            mode);

        if (!decision.IsWrite)
        {
            return new(decision.Kind, decision.Reason, Wrote: false, Attempts: 0, OpenError: null, ByteMatched: false);
        }

        // 現在値が自分の抑止状態なら異常終了後なので既存baselineを保持する。それ以外は、停止中に
        // 外部で変更された可能性を含む現在値をwrite前の新baselineにする。
        var currentIsSuppressed = G600LegacySuppression.IsAnyApplied(current!);
        var source = current!;
        if (currentIsSuppressed)
        {
            source = baseline!; // baselineなしはpolicyがRefuseAppliedWithoutBaselineで上で返している。
        }
        else
        {
            _baseline.SaveF3(current!);
        }
        var payload = G600LegacySuppression.Build(source, mode);
        var write = G600EvidenceWrite.TryWrite(_access, payload, _sleep, _maxAttempts, _settleMs);
        return ToResult(decision, write);
    }

    public G600LeftoverResult Restore()
    {
        var baseline = _baseline.LoadF3();
        var current = baseline is null
            ? null
            : G600EvidenceWrite.TryRead(_access, G600EvidenceWrite.ProfileReportIdF3, _sleep, _settleMs);
        var decision = G600LeftoverPolicy.DecideRestore(_coexistenceRunning(), current, baseline);

        if (!decision.IsWrite)
        {
            return new(decision.Kind, decision.Reason, Wrote: false, Attempts: 0, OpenError: null, ByteMatched: false);
        }

        var write = G600EvidenceWrite.TryWrite(_access, baseline!, _sleep, _maxAttempts, _settleMs);
        return ToResult(decision, write);
    }

    private static G600LeftoverResult ToResult(G600LeftoverDecision decision, G600EvidenceWriteResult write)
    {
        if (write.OpenError is not null)
        {
            return new(decision.Kind, decision.Reason, Wrote: true, write.Attempts, write.OpenError, ByteMatched: false);
        }

        if (!write.Matched)
        {
            return new(
                decision.Kind,
                $"{decision.Reason} write が {write.Attempts} 回で byte 一致しなかった。",
                Wrote: true,
                write.Attempts,
                OpenError: null,
                ByteMatched: false);
        }

        return new(decision.Kind, decision.Reason, Wrote: true, write.Attempts, OpenError: null, ByteMatched: true);
    }
}
