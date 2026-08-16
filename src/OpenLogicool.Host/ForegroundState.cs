namespace OpenLogicool.Host;

/// <summary>
/// foreground app 監視が観測している状態を明示する3値（APP-008）。
/// 「identity 不明時は Unknown Application へ明示遷移し、直前 profile を黙って継続しない」ことを
/// 観測可能にするための表示専用の分類であり、profile 選択規則（package→path→既定）自体は変えない。
/// </summary>
public enum ForegroundState
{
    /// <summary>foreground app が package/path のいずれかの明示関連付けに一致した。</summary>
    KnownMatched,

    /// <summary>foreground app の identity は識別できたが、明示関連付けが無く既定 profile を適用している。</summary>
    KnownDefault,

    /// <summary>foreground window/process の識別要素が一つも取得できず、既定 profile を適用している。</summary>
    UnknownApplication,
}

/// <summary>
/// ProfileSwitchKindOutcome の一致種別集合から ForegroundState を導出する pure function（APP-008）。
/// 判定規則そのもの（package→path→既定）は AppProfileResolver.ResolveWithReason が正であり、
/// ここではその結果（MatchKind）から表示状態を組み立てるだけで二重実装しない。
/// </summary>
public static class ForegroundStateClassifier
{
    /// <summary>
    /// 導出規則: 全種別が "identity-unavailable" → UnknownApplication／
    /// それ以外で "package" か "path" の一致が1種別でもあれば KnownMatched／
    /// どれも "default" のみ（かつ identity-unavailable が無い） → KnownDefault。
    /// outcomes が空（対象 device 種別なし）の場合は KnownDefault を返す
    /// （identity 不明の宣言はできず、既定適用中という保守的表示）。
    /// </summary>
    public static ForegroundState Classify(IReadOnlyList<ProfileSwitchKindOutcome> outcomes) =>
        Classify(outcomes.Select(outcome => outcome.MatchKind).ToArray());

    /// <summary>
    /// MatchKind 文字列集合（"package"/"path"/"default"/"identity-unavailable"）からの導出。
    /// resolver.ResolveWithReason を device 種別ごとに直接呼んだだけの場面（diagnostics 等・
    /// previous profile の文脈が無い場面）で使う。導出規則は <see cref="Classify(IReadOnlyList{ProfileSwitchKindOutcome})"/> と同一。
    /// </summary>
    public static ForegroundState Classify(IReadOnlyList<string> matchKinds)
    {
        if (matchKinds.Count == 0)
        {
            return ForegroundState.KnownDefault;
        }

        if (matchKinds.All(matchKind => matchKind == "identity-unavailable"))
        {
            return ForegroundState.UnknownApplication;
        }

        if (matchKinds.Any(matchKind => matchKind is "package" or "path"))
        {
            return ForegroundState.KnownMatched;
        }

        return ForegroundState.KnownDefault;
    }

    /// <summary>
    /// 状態遷移の検出（pure）。前回状態が無い（初回観測）場合も遷移として扱う——
    /// 「run 起動時に初期状態も1行表示」する要件のための唯一の分岐点。同一状態の継続では false。
    /// </summary>
    public static bool HasTransitioned(ForegroundState? previous, ForegroundState current) =>
        previous is null || previous.Value != current;

    /// <summary>ForegroundState の日本語表示（console log・diagnostics 共用）。</summary>
    public static string Describe(ForegroundState state, string? matchDetail = null) => state switch
    {
        ForegroundState.KnownMatched => matchDetail is null ? "一致 app" : $"一致 app（{matchDetail}）",
        ForegroundState.KnownDefault => "既定 app（identity 識別済み・関連付けなし）",
        ForegroundState.UnknownApplication => "Unknown Application（identity 取得不能・既定 profile 適用）",
        _ => state.ToString(),
    };
}
