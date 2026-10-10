using System.IO;
using System.Text;
using System.Text.Json;

namespace OpenLogicool.Host;

internal sealed record BotWorkerRun(string ScriptId, BotFunctionPlan Plan, string DatabasePath,
    string DataDirectory, string EvidenceDirectory);

internal static class BotWorkerCommand
{
    public static int Run(string[] arguments) => RunAsync(arguments).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string[] arguments)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        // 標準入力のWindows handleは同期の場合がある。独立したthreadで監視し、
        // 通常終了は読取の終了を待たない。readerとstopの寿命はこのprocessの寿命に揃える。
        var stop = new CancellationTokenSource();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(() => WatchStandardInputAsync(Console.In, stop, CancellationToken.None, ready));
        try
        {
            if (arguments.SequenceEqual(new[] { "--self-test" }))
            {
                ValidatePackages(AppContext.BaseDirectory);
                Console.WriteLine(JsonSerializer.Serialize(new { Protocol = BotWorkerPipe.Protocol }));
                return 0;
            }
            // Jobへの登録が済むまで実行を始めない。登録前に本体が死んだ時はEOFで終了する。
            await ready.Task.WaitAsync(stop.Token);
            string Required(string name)
            {
                var index = Array.IndexOf(arguments, name);
                return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1]
                    : throw new ArgumentException($"{name} が必要です。");
            }
            BotWorkerDpi.Configure(int.Parse(Required("--dpi-context"), System.Globalization.CultureInfo.InvariantCulture));
            using var client = await BotWorkerPipeClient.ConnectAsync(Required("--input-pipe"), Required("--query-pipe"),
                BotWorkerDpi.Snapshot(), stop.Token);
            var run = JsonSerializer.Deserialize<BotWorkerRun>(File.ReadAllText(Required("--request")))
                ?? throw new InvalidDataException("Bot実行の指定が空です。");
            var package = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "BotScripts"), "bot.json", SearchOption.AllDirectories)
                .Select(BotScriptPackage.Load).Single(package => package.Id == run.ScriptId);
            var target = WindowsGameTargetLocator.Locate(package.ProcessName);
            var result = await VisualKeyAssistRuntime.RunAsync(BuildArguments(run, package), client, client.Nano, target,
                $"window:bot:{target.ProcessId}", client.Token, client.Report, client.Physical, client.Mode);
            if (client.Fault is { } fault) throw new IOException("本体との通信が終了しました。", fault);
            File.WriteAllText(Path.Combine(run.EvidenceDirectory, "result.json"), JsonSerializer.Serialize(result));
            return 0;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 0; }
        catch (Exception error)
        {
            Console.Error.WriteLine("Botの実行に失敗しました: " + error);
            return 2;
        }
    }

    internal static async Task WatchStandardInputAsync(TextReader reader, CancellationTokenSource stop, CancellationToken token,
        TaskCompletionSource? ready = null)
    {
        try
        {
            if (ready is not null)
            {
                var first = await reader.ReadLineAsync(token);
                if (first is null) { await stop.CancelAsync(); return; }
                if (first != "start") throw new InvalidDataException("Botの起動合図が不正です。");
                ready.SetResult();
            }
            var buffer = new char[256];
            while (await reader.ReadAsync(buffer.AsMemory(), token) != 0) { }
            await stop.CancelAsync();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            Console.Error.WriteLine("Botの標準入力の監視に失敗しました: " + error.Message);
            await stop.CancelAsync();
        }
    }

    internal static void ValidatePackages(string directory)
    {
        var files = Directory.GetFiles(Path.Combine(directory, "BotScripts"), "bot.json", SearchOption.AllDirectories);
        if (files.Length == 0) throw new InvalidDataException("Bot設定がありません。");
        foreach (var file in files)
        {
            var package = BotScriptPackage.Load(file);
            _ = VisualKeyTemplate.Load(package.File("stop.png"));
            _ = VisualRecoveryProfile.Load(package.File("profile.json"));
            var functions = VisualProgressProfile.ListFunctions(package.File("progress.json"));
            _ = VisualProgressProfile.Load(package.File("progress.json"), functions.Select(function => function.Id).ToArray());
        }
    }

    private static string[] BuildArguments(BotWorkerRun run, BotScriptPackage package)
    {
        var arguments = new List<string> { "--db", Path.Combine(run.DataDirectory, run.ScriptId + ".db"),
            "--inhibit-image", package.File("stop.png"), "--cue-text", "Space", "--cue-text", "画面を押してください",
            "--keys", "Key:Space", "--recovery-profile", package.File("profile.json"), "--evidence", run.EvidenceDirectory,
            "--continue-on-review", "--pause-on-user-input", "--assistance-db", run.DatabasePath };
        arguments.AddRange(BotFunctionPlanner.Arguments(run.Plan, package.File("progress.json")));
        if (!package.TimedInputEnabled) arguments.Add("--no-timed-input");
        var directory = Path.GetDirectoryName(Path.GetFullPath(run.DatabasePath))!;
        var review = Path.Combine(directory, "bot-review-mcp.json");
        var judge = Path.Combine(directory, ScreenJudgeSettings.FileName);
        if (File.Exists(review)) arguments.AddRange(["--review-mcp", review]);
        if (File.Exists(judge)) arguments.AddRange(["--screen-judge", judge]);
        return arguments.ToArray();
    }
}
