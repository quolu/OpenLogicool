using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

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
        // GameLabはcapture検証用test fieldである。GPU compositor差を混ぜず、
        // WGCが実内容を取得できる決定的なsoftware renderへ固定する。
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        base.OnStartup(e);

        var seed = ParseSeed(e.Args);

        if (Array.IndexOf(e.Args, "--selftest") >= 0)
        {
            RunSelftest(seed);
            Shutdown(0);
            return;
        }

        var window = new MainWindow(seed, ParseOptionalArgument(e.Args, "--input-log"));
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

    private static string? ParseOptionalArgument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length
            ? Path.GetFullPath(args[index + 1])
            : null;
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
