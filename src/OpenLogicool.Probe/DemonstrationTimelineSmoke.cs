using System.Diagnostics;
using System.Text.Json;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Host;
using OpenLogicool.Playbooks;

namespace OpenLogicool.Probe;

/// <summary>Windows実画面とNano入力をpublic intentsへ通し、数秒間隔の往復記録を確認する。</summary>
internal static class DemonstrationTimelineSmoke
{
    public static int Run(string[] arguments)
    {
        var portIndex = Array.IndexOf(arguments, "--port");
        if (portIndex < 0 || portIndex + 1 >= arguments.Length) throw new ArgumentException("--port <COMn> が必要です。");
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "probe-output",
            $"demonstration-timeline-smoke-{DateTime.Now:yyyyMMdd-HHmmss-fff}"));
        Directory.CreateDirectory(directory);
        var reportPath = Path.Combine(directory, "report.json");
        var databasePath = Path.Combine(directory, "demo.db");
        try
        {
            using var window = DemonstrationRecorderSmoke.SelfWindow.Create("OpenLogicool 記録時刻の測定窓", 120, 120, 720, 520,
                "START", "MENU\nITEM\nBACK", toggleOnClick: true);
            var processName = Process.GetCurrentProcess().ProcessName;
            MacroTargetSettingsStore.ForDatabase(databasePath).Save(processName);
            using var recording = new HostDemonstrationRecordingIntents(databasePath,
                new WindowsDemonstrationLiveSessionFactory(databasePath), new DemonstrationRecordingGate());
            var timer = Stopwatch.StartNew();
            var started = recording.StartAsync("ページを開いて開始画面へ戻る").GetAwaiter().GetResult();
            var startMilliseconds = timer.ElapsedMilliseconds;
            var first = DemonstrationJourneySmoke.NanoClick(arguments[portIndex + 1], window.ClientBoundsOnScreen());
            Thread.Sleep(2_000);
            var second = DemonstrationJourneySmoke.NanoClick(arguments[portIndex + 1], window.ClientBoundsOnScreen());
            Thread.Sleep(2_000);
            var beforeAnalysis = recording.Status();
            Console.WriteLine($"開始 {startMilliseconds}ms、原本 {beforeAnalysis.TotalOperations} 操作。保存画像の解析へ進みます。");
            var stopped = recording.StopAsync().GetAwaiter().GetResult();
            var steps = recording.ListSteps(started.SessionId);
            MacroCatalogItem? macro = null;
            string? compilationError = null;
            try { macro = recording.CreateMacroFromSession(started.SessionId); }
            catch (InvalidOperationException exception) { compilationError = exception.Message; }
            var passed = first.CursorMatched && second.CursorMatched && startMilliseconds < 5_000
                && beforeAnalysis.TotalOperations == 2 && stopped.OperationCount == 2
                && steps.Count == 2 && steps.All(item => item.TransitionLabel == "画面が変わった") && macro?.StepCount == 2;
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new
            {
                probe = "demonstration-timeline-smoke", started.SessionId, startMilliseconds,
                additionalPauseMilliseconds = 2_000, beforeAnalysis, stopped, steps, macro, compilationError,
                SendInput = 0, ComputerUse = 0, CloudApi = 0, passed,
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"{(passed ? "確認済み" : "不成立")}: 保存 {stopped.OperationCount}、マクロ {macro?.StepCount}、判定 {string.Join('、', steps.Select(item => item.TransitionLabel))}");
            Console.WriteLine($"report: {reportPath}");
            return passed ? 0 : 1;
        }
        catch (Exception exception)
        {
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { probe = "demonstration-timeline-smoke", passed = false,
                error = exception.ToString(), SendInput = 0, ComputerUse = 0, CloudApi = 0 }, new JsonSerializerOptions { WriteIndented = true }));
            Console.Error.WriteLine(exception.Message);
            Console.WriteLine($"report: {reportPath}");
            return 1;
        }
    }
}
