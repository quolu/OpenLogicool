namespace OpenLogicool.GameLab;

public enum GameLabStateId
{
    MainMenu,
    EventPopup,
    RewardList,
    ClaimConfirm,
    ClaimDone,
    UnknownGlitch,
}

public sealed record GameLabOracleEntry(
    long Seq,
    double MonotonicMs,
    string StateId,
    string Cause,
    string ScenarioId);

public sealed class GameLabStateMachine
{
    private static readonly IReadOnlyDictionary<(GameLabStateId From, string Action), GameLabStateId> Transitions =
        new Dictionary<(GameLabStateId, string), GameLabStateId>
        {
            [(GameLabStateId.MainMenu, "OpenEvent")] = GameLabStateId.EventPopup,
            [(GameLabStateId.EventPopup, "ClosePopup")] = GameLabStateId.MainMenu,
            [(GameLabStateId.MainMenu, "OpenRewards")] = GameLabStateId.RewardList,
            [(GameLabStateId.RewardList, "SelectReward")] = GameLabStateId.ClaimConfirm,
            [(GameLabStateId.ClaimConfirm, "Confirm")] = GameLabStateId.ClaimDone,
            [(GameLabStateId.ClaimConfirm, "Cancel")] = GameLabStateId.RewardList,
        };

    private readonly ScenarioManifest _scenario;
    private readonly List<GameLabOracleEntry> _oracle = new();
    private uint _randomState;
    private PendingTransition? _pendingTransition;
    private long _sequence;

    public GameLabStateMachine(ScenarioManifest scenario)
    {
        _scenario = scenario;
        _randomState = unchecked((uint)scenario.Seed);
        CurrentState = ParseState(scenario.InitialState);
        MonotonicMs = scenario.VirtualClock.InitialMonotonicMs;
        Record("scenario:start");

        if (Roll(scenario.Popup))
        {
            CurrentState = GameLabStateId.EventPopup;
            Record("auto:popup");
        }
    }

    public GameLabStateId CurrentState { get; private set; }

    public double MonotonicMs { get; private set; }

    public bool RewardClaimed { get; private set; }

    public IReadOnlyList<GameLabOracleEntry> Oracle => _oracle;

    public static bool HasTransition(GameLabStateId from, string action) =>
        Transitions.ContainsKey((from, action));

    public bool TryAction(string action)
    {
        if (_pendingTransition is not null || !Transitions.TryGetValue((CurrentState, action), out var target))
        {
            return false;
        }

        if (Roll(_scenario.Unknown))
        {
            CurrentState = GameLabStateId.UnknownGlitch;
            Record("auto:unknown-glitch");
            return true;
        }

        _pendingTransition = new PendingTransition(
            target,
            $"action:{action}",
            MonotonicMs + _scenario.Delay.TransitionDelayMs);
        return true;
    }

    public void Tick(double elapsedMs)
    {
        if (elapsedMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedMs));
        }

        var previousDay = DayNumber(MonotonicMs);
        MonotonicMs += elapsedMs;

        if (DayNumber(MonotonicMs) > previousDay)
        {
            _pendingTransition = null;
            RewardClaimed = false;
            CurrentState = GameLabStateId.MainMenu;
            Record("auto:daily-reset");
            return;
        }

        if (_pendingTransition is { } pending && MonotonicMs >= pending.DueMonotonicMs)
        {
            _pendingTransition = null;
            CurrentState = pending.Target;
            if (CurrentState == GameLabStateId.ClaimDone)
            {
                RewardClaimed = true;
            }

            Record(pending.Cause);
        }
    }

    public void ManualIntervention()
    {
        _pendingTransition = null;
        CurrentState = GameLabStateId.MainMenu;
        Record("manual-intervention");
    }

    private long DayNumber(double monotonicMs) =>
        (long)Math.Floor(monotonicMs / _scenario.VirtualClock.DayLengthMs);

    private bool Roll(SeededChance chance)
    {
        if (chance.Numerator == 0)
        {
            return false;
        }

        _randomState = unchecked((1664525U * _randomState) + 1013904223U);
        return _randomState % (uint)chance.Denominator < chance.Numerator;
    }

    private void Record(string cause)
    {
        _sequence++;
        _oracle.Add(new GameLabOracleEntry(
            _sequence,
            MonotonicMs,
            ToStableId(CurrentState),
            cause,
            _scenario.ScenarioId));
    }

    private static GameLabStateId ParseState(string stateId) => stateId switch
    {
        "state.main-menu" => GameLabStateId.MainMenu,
        "state.main-menu.event-popup" => GameLabStateId.EventPopup,
        "state.reward-list" => GameLabStateId.RewardList,
        "state.claim-confirm" => GameLabStateId.ClaimConfirm,
        "state.claim-done" => GameLabStateId.ClaimDone,
        "state.unknown-glitch" => GameLabStateId.UnknownGlitch,
        _ => throw new ArgumentOutOfRangeException(nameof(stateId), stateId, null),
    };

    private static string ToStableId(GameLabStateId state) => state switch
    {
        GameLabStateId.MainMenu => "state.main-menu",
        GameLabStateId.EventPopup => "state.main-menu.event-popup",
        GameLabStateId.RewardList => "state.reward-list",
        GameLabStateId.ClaimConfirm => "state.claim-confirm",
        GameLabStateId.ClaimDone => "state.claim-done",
        GameLabStateId.UnknownGlitch => "state.unknown-glitch",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    private sealed record PendingTransition(GameLabStateId Target, string Cause, double DueMonotonicMs);
}
