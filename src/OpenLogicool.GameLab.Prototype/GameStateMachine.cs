using System.Diagnostics;

namespace OpenLogicool.GameLab.Prototype;

/// <summary>
/// GameLab prototype の決定的状態機械。UI・oracle 書出しから分離（テスト対象にするため）。
/// 同じ seed ＋同じ操作列は同じ状態列を生む（wall clock は入力に使わない）。
/// </summary>
public sealed class GameStateMachine
{
    // 任意state → unknown-glitch の発生確率（button 遷移1回ごとの抽選）。
    private const double GlitchProbability = 0.2;

    // 起動直後に main-menu → event-popup へ自動遷移する確率。
    private const double InitialPopupProbability = 0.2;

    // 遷移表: claim-done を From に持つ entry は存在しない（逆遷移が型で不可能なことの根拠）。
    private static readonly IReadOnlyDictionary<(GameStateId From, string Button), GameStateId> Transitions =
        new Dictionary<(GameStateId, string), GameStateId>
        {
            [(GameStateId.MainMenu, "OpenEvent")] = GameStateId.EventPopup,
            [(GameStateId.EventPopup, "ClosePopup")] = GameStateId.MainMenu,
            [(GameStateId.MainMenu, "OpenRewards")] = GameStateId.RewardList,
            [(GameStateId.RewardList, "SelectReward")] = GameStateId.ClaimConfirm,
            [(GameStateId.ClaimConfirm, "Confirm")] = GameStateId.ClaimDone,
            [(GameStateId.ClaimConfirm, "Cancel")] = GameStateId.RewardList,
        };

    private readonly Random _random;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<OracleEntry> _history = new();
    private int _sequence;

    public GameStateMachine(int seed)
    {
        _random = new Random(seed);

        CurrentState = GameStateId.MainMenu;
        Record("auto:seed");

        if (_random.NextDouble() < InitialPopupProbability)
        {
            CurrentState = GameStateId.EventPopup;
            Record("auto:seed");
        }
    }

    public GameStateId CurrentState { get; private set; }

    public IReadOnlyList<OracleEntry> History => _history;

    /// <summary>
    /// 遷移ボタンを押す。現在 state に対して定義されていないボタン（claim-done からの
    /// 全ボタンを含む）は false を返し、state・oracle とも変化しない。
    /// </summary>
    public bool TryButton(string buttonName)
    {
        if (!Transitions.TryGetValue((CurrentState, buttonName), out var target))
        {
            return false;
        }

        if (_random.NextDouble() < GlitchProbability)
        {
            CurrentState = GameStateId.UnknownGlitch;
            Record("auto:seed");
            return true;
        }

        CurrentState = target;
        Record($"button:{buttonName}");
        return true;
    }

    /// <summary>
    /// キーボード入力等、自動化外の手動介入。unknown-glitch を含むどの state からも
    /// main-menu へ復帰する（回復ボタンが存在しないための唯一の復帰経路）。
    /// </summary>
    public void ManualIntervention()
    {
        CurrentState = GameStateId.MainMenu;
        Record("manual-intervention");
    }

    private void Record(string cause)
    {
        _sequence++;
        _history.Add(new OracleEntry(_sequence, _clock.Elapsed.TotalMilliseconds, GameStateIdVocabulary.ToStableId(CurrentState), cause));
    }
}
