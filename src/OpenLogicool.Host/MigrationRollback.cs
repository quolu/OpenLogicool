using OpenLogicool.Devices.G600;
using OpenLogicool.Profiles;

namespace OpenLogicool.Host;

/// <summary>migration の取り消し／G600 baseline 復元の結果。</summary>
public enum MigrationRollbackOutcome
{
    DryRunCancelled,
    G600BaselineRestored,
    G600RestoreNotCompleted,
}

/// <summary>元の LGS profile を変更しない migration cancel／restore の結果。</summary>
public sealed record MigrationRollbackResult(
    MigrationRollbackOutcome Outcome,
    bool ApplyStarted,
    bool OriginalLgsProfilePreserved,
    int ConvertibleCount,
    int UnsupportedCount,
    G600LeftoverResult? G600Result);

/// <summary>
/// LGS migration の取消と G600 baseline 復元の製品口。
/// dry-run はここから apply へ進まず、G600 の実際の write／readback は既存 G600LeftoverSession だけへ委譲する。
/// </summary>
public sealed class MigrationRollback
{
    private readonly Func<G600LeftoverResult> _restoreG600;

    public MigrationRollback(G600LeftoverSession leftover)
        : this((leftover ?? throw new ArgumentNullException(nameof(leftover))).Restore)
    {
    }

    /// <summary>テストまたは Host composition 用の restore port。</summary>
    public MigrationRollback(Func<G600LeftoverResult> restoreG600)
    {
        _restoreG600 = restoreG600 ?? throw new ArgumentNullException(nameof(restoreG600));
    }

    /// <summary>dry-run の結果を破棄して migration を取り消す。LGS profile、device、apply を変更しない。</summary>
    public static MigrationRollbackResult CancelDryRun(LgsXmlDryRunReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new(
            MigrationRollbackOutcome.DryRunCancelled,
            ApplyStarted: false,
            OriginalLgsProfilePreserved: true,
            ConvertibleCount: report.Convertible.Count,
            UnsupportedCount: report.Unsupported.Count,
            G600Result: null);
    }

    /// <summary>既存 leftover session の restore だけを呼び、baseline への復元結果をそのまま返す。</summary>
    public MigrationRollbackResult RestoreG600Baseline()
    {
        var result = _restoreG600();
        var restored = (result.Kind is G600LeftoverKind.Restore or G600LeftoverKind.AlreadyRestored)
            && !result.IsHardFailure;

        return new(
            restored ? MigrationRollbackOutcome.G600BaselineRestored : MigrationRollbackOutcome.G600RestoreNotCompleted,
            ApplyStarted: false,
            OriginalLgsProfilePreserved: true,
            ConvertibleCount: 0,
            UnsupportedCount: 0,
            G600Result: result);
    }
}
