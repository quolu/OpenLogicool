namespace OpenLogicool.Host;

/// <summary>
/// キーボードとマウスの手入力を観測する元。Botと同じprocessで見る元と、管理者権限の別processで見る元がある。
/// </summary>
internal interface IRawUserInputSource : IDisposable
{
    UserInputPauseSnapshot Snapshot();
}

/// <summary>
/// Botが手を止めるかを決める。人の手の入力装置すべてを対象にし、キーボードとマウス（観測元）・
/// G13／G600の物理ボタン（常駐の観測）・Botが矢印を動かせなかった合図を合わせる。
/// どれかが押下中か、最後の入力から無入力の時間が過ぎていなければ一時停止にする。
/// Bot自身の入力はNanoから出るので、観測元が入力元の機器で見分けて数えない。
/// </summary>
internal sealed class BotUserInputGate : IDisposable
{
    private readonly IRawUserInputSource raw;
    private readonly Func<ResidentPhysicalInput?>? physicalInput;
    private readonly UserInputPauseState local;
    private readonly PhysicalInputPauseBridge bridge;

    public BotUserInputGate(IRawUserInputSource raw, Func<ResidentPhysicalInput?>? physicalInput, Func<long> milliseconds,
        string sourceDescription)
    {
        this.raw = raw;
        this.physicalInput = physicalInput;
        SourceDescription = sourceDescription;
        // G13／G600の物理ボタンは仮想キーではないので、OSでなく常駐の観測で押下を確かめる。
        PhysicalInputPauseBridge? created = null;
        local = new(milliseconds, code => code == PhysicalInputPauseBridge.Code && created!.IsHeld);
        bridge = created = new(local);
    }

    /// <summary>キーボードとマウスをどこで観測しているか（記録用）。</summary>
    public string SourceDescription { get; }

    /// <summary>
    /// 対象のゲームがBotより高い権限で動いている時は、同じprocessからキーボードとマウスの入力が見えない。
    /// その時は管理者権限の監視processで観測する。対象の権限を読めない時も、見えているとは言えないので同じにする。
    /// </summary>
    public static BotUserInputGate Create(SerialHidCandidate nano, Func<ResidentPhysicalInput?>? physicalInput, int targetProcessId)
    {
        var own = ProcessIntegrity.Current();
        var target = ProcessIntegrity.Level(targetProcessId);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var description = FormattableString.Invariant($"対象の整合性=0x{target ?? 0:X}（読めない時は0） Botの整合性=0x{own:X}");
        return ProcessIntegrity.NeedsElevatedWatch(target, own)
            ? new(new ElevatedUserInputWatch(nano, target ?? ProcessIntegrity.High), physicalInput, () => clock.ElapsedMilliseconds,
                "管理者権限の監視process " + description)
            : new(new WindowsUserInputMonitor(nano), physicalInput, () => clock.ElapsedMilliseconds, "Botと同じprocess " + description);
    }

    public UserInputPauseSnapshot Snapshot()
    {
        // G13／G600の物理ボタンは、Raw Inputでなく常駐の観測から取る（Raw Inputの登録は増やさない）。
        if (physicalInput is not null) bridge.Apply(physicalInput());
        return Merge(raw.Snapshot(), local.Snapshot());
    }

    /// <summary>
    /// Botがpointerを動かせなかった時に呼ぶ。矢印は利用者か別の操作が握っているので、手入力があった時と同じだけ待つ。
    /// </summary>
    public void PointerHeldByOther() => local.Activity();

    /// <summary>二つの観測を一つにまとめる。押下はどちらかにあれば押下中、無入力の時間は短い方を使う。</summary>
    internal static UserInputPauseSnapshot Merge(UserInputPauseSnapshot first, UserInputPauseSnapshot second) =>
        new(first.Paused || second.Paused, first.HeldCount + second.HeldCount,
            Math.Min(first.IdleMilliseconds, second.IdleMilliseconds), first.UserEvents + second.UserEvents,
            first.NanoEvents + second.NanoEvents, first.LostReleases + second.LostReleases,
            [.. (first.HeldCodes ?? []).Concat(second.HeldCodes ?? []).Distinct().Order()]);

    public void Dispose() => raw.Dispose();
}
