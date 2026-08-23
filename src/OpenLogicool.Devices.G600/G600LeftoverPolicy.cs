namespace OpenLogicool.Devices.G600;

/// <summary>
/// B変種残置運用の判断（I/O なし）。
/// 適用は Input Studio が G600 を管理している間だけ、解除は管理終了時。
/// foreground 切替では呼ばない（MAP-010）。
/// </summary>
public enum G600LeftoverKind
{
    Apply,
    AlreadyApplied,
    Restore,
    AlreadyRestored,
    SkipNotManaged,
    SkipNothingToRestore,
    RefuseCoexistence,
    RefuseNoDevice,
    RefuseAppliedWithoutBaseline,
}

public sealed record G600LeftoverDecision(G600LeftoverKind Kind, string Reason)
{
    public bool IsWrite => Kind is G600LeftoverKind.Apply or G600LeftoverKind.Restore;

    public bool IsHardFailure => Kind == G600LeftoverKind.RefuseAppliedWithoutBaseline;
}

public static class G600LeftoverPolicy
{
    public static G600LeftoverDecision DecideApply(
        bool managed,
        bool devicePresent,
        bool coexistenceRunning,
        byte[]? currentF3,
        byte[]? baselineF3,
        G600LegacySuppressionMode mode = G600LegacySuppressionMode.IntermediateUsage)
    {
        if (!managed)
        {
            return new(G600LeftoverKind.SkipNotManaged, "G600 は profile 管理下にない。");
        }

        if (coexistenceRunning)
        {
            return new(G600LeftoverKind.RefuseCoexistence, "LGS / G HUB / Options+ が実行中のため onboard を書かない。");
        }

        if (!devicePresent || currentF3 is null)
        {
            return new(G600LeftoverKind.RefuseNoDevice, "G600 の feature report を読めない。");
        }

        if (G600LegacySuppression.IsApplied(currentF3, mode))
        {
            if (baselineF3 is null)
            {
                return new(
                    G600LeftoverKind.RefuseAppliedWithoutBaseline,
                    "既に legacy 抑止済みだが復元元がない。probe g600-restore-retry で戻してから再実行すること。");
            }

            return new(G600LeftoverKind.AlreadyApplied, "選択中の legacy 抑止は既に残置されている。");
        }

        if (baselineF3 is null && G600LegacySuppression.IsAnyApplied(currentF3))
        {
            return new(
                G600LeftoverKind.RefuseAppliedWithoutBaseline,
                "別方式の legacy 抑止が残っているが復元元がない。probe g600-restore-retry で戻してから再実行すること。");
        }

        return mode switch
        {
            G600LegacySuppressionMode.IntermediateUsage =>
                new(G600LeftoverKind.Apply, "side 割当を中間 usage へ書き換えて残置する。"),
            G600LegacySuppressionMode.NoOutput =>
                new(G600LeftoverKind.Apply, "G6〜G20 の onboard 出力を無効化して残置する。"),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "unknown G600 legacy suppression mode"),
        };
    }

    public static G600LeftoverDecision DecideRestore(
        bool coexistenceRunning,
        byte[]? currentF3,
        byte[]? baselineF3)
    {
        if (baselineF3 is null)
        {
            return new(G600LeftoverKind.SkipNothingToRestore, "復元する baseline がない。");
        }

        if (coexistenceRunning)
        {
            return new(G600LeftoverKind.RefuseCoexistence, "LGS / G HUB / Options+ が実行中のため復元を見送る。");
        }

        if (currentF3 is null)
        {
            return new(G600LeftoverKind.RefuseNoDevice, "G600 の feature report を読めない。");
        }

        if (currentF3.AsSpan().SequenceEqual(baselineF3))
        {
            return new(G600LeftoverKind.AlreadyRestored, "F3 は既に baseline と一致している。");
        }

        return new(G600LeftoverKind.Restore, "baseline の F3 を書き戻して出荷割当を戻す。");
    }
}
