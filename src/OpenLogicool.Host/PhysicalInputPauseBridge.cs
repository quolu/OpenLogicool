namespace OpenLogicool.Host;

/// <summary>
/// 常駐が観測した G13／G600 の物理ボタンを、Botの手入力の状態へ渡す。
/// G13／G600 の割当キーはNanoから出るため、Raw Inputの入力元がNanoの入力は手入力と見分けられない。
/// 常駐は物理ボタンの edge を直接知っているので、こちらを手入力として扱う。
/// </summary>
internal sealed class PhysicalInputPauseBridge(UserInputPauseState state)
{
    /// <summary>押下状態の入力元。キーボード・マウスのRaw Input（実機のhandle）・合成入力（-2）と重ならない。</summary>
    public static readonly nint Source = new(-3);

    /// <summary>押下状態の符号。仮想キー番号（〜0xFF）とマウスのボタン（0x10000 + 番号）の外に置く。</summary>
    public const int Code = 0x20000;

    private readonly Lock gate = new();
    private long? edgeCount;
    private volatile bool held;

    /// <summary>
    /// 物理ボタンがいま押されているか。UserInputPauseState の押下確認（状態の lock の内側）から呼ばれるため、
    /// lock を取らずに読む。
    /// </summary>
    public bool IsHeld => held;

    /// <summary>
    /// 観測を1回反映する。最初の観測は基準として覚えるだけで、件数の増加は手入力にしない
    /// （ただし押下中なら押下として扱う）。null は常駐が無い状態で、押下中として記録した分だけ離す。
    /// </summary>
    public void Apply(ResidentPhysicalInput? reading)
    {
        lock (gate)
        {
            if (reading is not { } current)
            {
                // 常駐が止まった。再開後の件数は 0 から数え直しになるため、基準も捨てる。
                edgeCount = null;
                Release();
                return;
            }

            if (edgeCount is { } previous && previous != current.EdgeCount)
            {
                state.Activity();
            }

            edgeCount = current.EdgeCount;

            var nowHeld = current.HeldCount > 0;
            if (nowHeld != held)
            {
                state.Button(Source, Code, nowHeld);
                held = nowHeld;
            }
        }
    }

    private void Release()
    {
        if (!held)
        {
            return;
        }

        state.Button(Source, Code, false);
        held = false;
    }
}
