namespace OpenLogicool.Desktop;

/// <summary>Game Operator の公開 support matrix で使う根拠状態。</summary>
public enum GameOperatorSupportStatus
{
    Supported,
    StrongInference,
    Unverified,
    Unsupported,
}

/// <summary>Game Operator の公開 capability 1行。</summary>
public sealed record GameOperatorSupportEntry(
    string Capability,
    GameOperatorSupportStatus Status,
    string Evidence,
    string Detail);

/// <summary>
/// Game Operator Public Gate の公開 matrix。
/// GameLab で確認した能力と、provider 未選定・実 game 未確認の能力を同じ claim に混ぜない。
/// </summary>
public static class GameOperatorSupportMatrix
{
    public const string PublicClaim = "Game Operator Preview";

    public static IReadOnlyList<GameOperatorSupportEntry> Entries { get; } =
    [
        new(
            "GameLab での crash boundary、停止、修正、再開を含む Durable Automation",
            GameOperatorSupportStatus.Supported,
            "Phase 4 Exit の focused crash matrix",
            "Supported は GameLab 環境に限定し、実 game の自律実行を含まない。"),
        new(
            "AI proposal の schema、catalog、state、risk の dispatch 前拒否",
            GameOperatorSupportStatus.Supported,
            "Phase 6 ProposalReject focused test",
            "拒否口は pure gate であり、AI は Input、device API、SQLite へ直接到達しない。"),
        new(
            "Data Flow contract による frame、OCR、journal、evidence crop のローカル処理境界",
            GameOperatorSupportStatus.Supported,
            "2026-08-24 オーナー裁定と STEP 0 local-only contract test",
            "full-screen frame の永続保存は既定 OFF。AI推論目的の外部送信経路と外部AI API key設定はなく、外部AI API費用は0。"),
        new(
            "game ごとの policy record による Assist／Auto の gate",
            GameOperatorSupportStatus.Supported,
            "Phase 7 GamePolicyGate focused test",
            "Unverified、Changed、InterpretationUnknown の policy record は Assist／Auto を拒否する。実 ToS の解釈や許可を意味しない。"),
        new(
            "ローカルAI provider と model",
            GameOperatorSupportStatus.Unverified,
            "ローカルprovider／modelはPhase 9 G0で未選定",
            "OpenAI APIを含む従量課金型の外部AI APIとcloud fallbackは不採用。ローカル方式の実測完了まではUnverified。"),
        new(
            "実 game の Observe Only と game policy の live 確認",
            GameOperatorSupportStatus.Unverified,
            "Phase 7 H は実 game 窓なし",
            "一般対応と表示せず、対象 game、version、region、anti-cheat、mode ごとの独立確認が必要である。"),
        new(
            "実 game 用 Verified Autonomous Playbook",
            GameOperatorSupportStatus.Unverified,
            "Phase 8B t09 live verified session は未確認",
            "GameLab で得た Verified 根拠は実 game 環境へ継承しない。"),
    ];

    public static IReadOnlyList<GameOperatorSupportEntry> SupportedEntries =>
        Entries.Where(entry => entry.Status == GameOperatorSupportStatus.Supported).ToArray();
}
