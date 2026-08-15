namespace OpenLogicool.GameLab.Prototype;

/// <summary>
/// GameLab prototype の 6 state（docs/gamelab-prototype-spec.md §状態機械）。
/// </summary>
public enum GameStateId
{
    MainMenu,
    EventPopup,
    RewardList,
    ClaimConfirm,
    ClaimDone,
    UnknownGlitch,
}

/// <summary>
/// GameStateId と Knowledge Pack の stable state ID（"state.main-menu" 等）の対応。
/// fixtures/contracts/observation-result.sample.json の "state.main-menu.event-popup" に合わせる。
/// </summary>
public static class GameStateIdVocabulary
{
    public static string ToStableId(GameStateId state) => state switch
    {
        GameStateId.MainMenu => "state.main-menu",
        GameStateId.EventPopup => "state.main-menu.event-popup",
        GameStateId.RewardList => "state.reward-list",
        GameStateId.ClaimConfirm => "state.claim-confirm",
        GameStateId.ClaimDone => "state.claim-done",
        GameStateId.UnknownGlitch => "state.unknown-glitch",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };
}
