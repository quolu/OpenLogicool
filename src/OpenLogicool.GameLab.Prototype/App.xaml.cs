using System.Text.Json;
using System.Windows;

namespace OpenLogicool.GameLab.Prototype;

public partial class App : Application
{
    // --selftest が UI なしで機械確認する固定操作列（仕様: CLI §--selftest）。
    private static readonly string[] SelftestButtons =
    {
        "OpenEvent", "ClosePopup", "OpenRewards", "SelectReward", "Confirm",
    };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var seed = ParseSeed(e.Args);

        if (Array.IndexOf(e.Args, "--selftest") >= 0)
        {
            RunSelftest(seed);
            Shutdown(0);
            return;
        }

        var window = new MainWindow(seed);
        window.Show();
    }

    private static int ParseSeed(string[] args)
    {
        var index = Array.IndexOf(args, "--seed");
        if (index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var seed))
        {
            return seed;
        }

        return 1;
    }

    private static void RunSelftest(int seed)
    {
        var machine = new GameStateMachine(seed);

        foreach (var button in SelftestButtons)
        {
            machine.TryButton(button);
        }

        var oraclePath = OracleWriter.NewFilePath(seed);
        OracleWriter.AppendAll(oraclePath, machine.History);

        var result = new
        {
            finalState = GameStateIdVocabulary.ToStableId(machine.CurrentState),
            oracleLines = machine.History.Count,
        };
        Console.WriteLine(JsonSerializer.Serialize(result));
    }
}
