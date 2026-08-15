using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenLogicool.GameLab.Prototype;

public partial class MainWindow : Window
{
    // state ごとの遷移ボタン（UI 表示専用。実際に許可される遷移は GameStateMachine の遷移表が正）。
    private static readonly IReadOnlyDictionary<GameStateId, string[]> ButtonsByState = new Dictionary<GameStateId, string[]>
    {
        [GameStateId.MainMenu] = new[] { "OpenEvent", "OpenRewards" },
        [GameStateId.EventPopup] = new[] { "ClosePopup" },
        [GameStateId.RewardList] = new[] { "SelectReward" },
        [GameStateId.ClaimConfirm] = new[] { "Confirm", "Cancel" },
        [GameStateId.ClaimDone] = Array.Empty<string>(),
        [GameStateId.UnknownGlitch] = Array.Empty<string>(),
    };

    // state ごとの背景色（recognizer fixture を単純にするため state ごとに変える）。
    private static readonly IReadOnlyDictionary<GameStateId, Color> BackgroundByState = new Dictionary<GameStateId, Color>
    {
        [GameStateId.MainMenu] = Colors.SteelBlue,
        [GameStateId.EventPopup] = Colors.DarkOrange,
        [GameStateId.RewardList] = Colors.SeaGreen,
        [GameStateId.ClaimConfirm] = Colors.MediumPurple,
        [GameStateId.ClaimDone] = Colors.Gold,
        [GameStateId.UnknownGlitch] = Colors.Crimson,
    };

    private readonly GameStateMachine _machine;
    private readonly string _oraclePath;
    private int _writtenCount;

    public MainWindow(int seed)
    {
        InitializeComponent();

        _machine = new GameStateMachine(seed);
        _oraclePath = OracleWriter.NewFilePath(seed);
        FlushOracle();

        KeyDown += OnKeyDown;

        Render();
    }

    private void OnButtonClick(object sender, RoutedEventArgs e)
    {
        var buttonName = (string)((Button)sender).Tag;
        _machine.TryButton(buttonName);
        FlushOracle();
        Render();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        _machine.ManualIntervention();
        FlushOracle();
        Render();
    }

    private void FlushOracle()
    {
        var history = _machine.History;
        for (; _writtenCount < history.Count; _writtenCount++)
        {
            OracleWriter.Append(_oraclePath, history[_writtenCount]);
        }
    }

    private void Render()
    {
        var state = _machine.CurrentState;

        Background = new SolidColorBrush(BackgroundByState[state]);
        StateLabel.Text = GameStateIdVocabulary.ToStableId(state);

        ButtonPanel.Children.Clear();
        foreach (var buttonName in ButtonsByState[state])
        {
            var button = new Button
            {
                Content = buttonName,
                Tag = buttonName,
                Margin = new Thickness(8),
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 16,
            };
            button.Click += OnButtonClick;
            ButtonPanel.Children.Add(button);
        }
    }
}
