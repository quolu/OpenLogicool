using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace OpenLogicool.Host;

internal static class BotWorkerProcess
{
    private static readonly TimeSpan StopDeadline = TimeSpan.FromSeconds(5);

    internal static string CurrentVersion(string hostDirectory)
    {
        var root = Path.GetFullPath(Path.Combine(hostDirectory, "..", "OpenLogicool.Bot"));
        var current = Path.Combine(root, "current.txt");
        if (!File.Exists(current)) throw new FileNotFoundException("Botが未導入です。install-bot.ps1で導入してください。", current);
        var version = File.ReadAllText(current).Trim();
        if (version.Length == 0 || version != Path.GetFileName(version) || version is "." or "..")
            throw new InvalidDataException("Botのcurrent.txtが不正です。");
        var directory = Path.Combine(root, version);
        if (!File.Exists(Path.Combine(directory, "OpenLogicool.Host.exe")))
            throw new FileNotFoundException("指定されたBotの版に実行ファイルがありません。", directory);
        return directory;
    }

    public static async Task<BotScriptResult> RunAsync(BotWorkerRun run, INanoGameInputDevice device,
        SerialHidCandidate nano, Func<ResidentPhysicalInput?>? physical, Func<string?> mode,
        Action<JsonElement> report, CancellationToken token)
    {
        var active = AcquireCurrentVersion(AppContext.BaseDirectory);
        var version = active.Directory;
        using var marker = active.Marker;
        var requestPath = Path.Combine(run.EvidenceDirectory, "worker-request.json");
        File.WriteAllText(requestPath, JsonSerializer.Serialize(run));
        var dpiContext = BotWorkerDpi.Context();
        await using var server = new BotWorkerPipeServer(device, nano, BotWorkerDpi.Snapshot(), physical, mode, report);
        using var job = new BotWorkerJob();
        var start = new ProcessStartInfo(Path.Combine(version, "OpenLogicool.Host.exe"))
        {
            WorkingDirectory = version, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        foreach (var argument in new[] { "bot-worker", "--input-pipe", server.InputName, "--query-pipe", server.QueryName,
            "--request", requestPath, "--dpi-context", dpiContext.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            start.ArgumentList.Add(argument);
        token.ThrowIfCancellationRequested();
        using var child = Process.Start(start) ?? throw new IOException("Botの子processを起動できませんでした。");
        var exited = child.WaitForExitAsync();
        // 排出は停止待ちと独立して続ける。子の出力をファイルへ残し、コンソールの詰まりへ依存しない。
        var stdout = DrainAsync(child.StandardOutput, Path.Combine(run.EvidenceDirectory, "worker-stdout.log"));
        var stderr = DrainAsync(child.StandardError, Path.Combine(run.EvidenceDirectory, "worker-stderr.log"));
        try
        {
            job.Add(child);
            await child.StandardInput.WriteLineAsync("start");
            await child.StandardInput.FlushAsync();
            using var registration = token.Register(server.Stop);
            await Task.WhenAny(exited, server.Stopped, stdout, stderr);
            server.Stop();
            child.StandardInput.Close();
            await FinishChildAsync(child, exited, run.EvidenceDirectory);
            await server.Completion;
            await Task.WhenAll(stdout, stderr);
            token.ThrowIfCancellationRequested();
            if (child.ExitCode != 0) throw new IOException($"Botが異常終了しました（終了コード {child.ExitCode}）。{run.EvidenceDirectory}");
            if (server.Fault is { } fault) throw new IOException("Botとの通信が終了しました: " + fault.Message, fault);
            var resultPath = Path.Combine(run.EvidenceDirectory, "result.json");
            var json = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(resultPath));
            return new(json.TryGetProperty("NeedsReview", out var review) && review.GetBoolean(),
                json.TryGetProperty("Detail", out var detail) ? detail.GetString()! : "実行が終了しました。");
        }
        finally
        {
            server.Stop();
            child.StandardInput.Close();
            await FinishChildAsync(child, exited, run.EvidenceDirectory);
            // 子の終了では番を空けない。受付済み入力と両パイプの処理を回収してから呼出元へ戻す。
            await server.Completion;
            await Task.WhenAll(stdout, stderr);
        }
    }

    private static async Task FinishChildAsync(Process child, Task exited, string evidence)
    {
        if (await Task.WhenAny(exited, Task.Delay(StopDeadline)) != exited)
        {
            File.WriteAllText(Path.Combine(evidence, "worker-stop-timeout.json"), JsonSerializer.Serialize(new
            {
                Reason = "停止の合図から5秒以内にBotが終了しなかったため、子processを終了します。",
                ProcessId = child.Id, DeadlineMilliseconds = StopDeadline.TotalMilliseconds,
            }));
            if (!child.HasExited) child.Kill(entireProcessTree: true);
        }
        await exited;
    }

    private static async Task DrainAsync(StreamReader reader, string path)
    {
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
        await using var writer = new StreamWriter(file, new UTF8Encoding(false));
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer)) != 0) await writer.WriteAsync(buffer.AsMemory(0, count));
    }

    internal static (string Directory, FileStream Marker) AcquireCurrentVersion(string hostDirectory)
    {
        var root = Path.GetFullPath(Path.Combine(hostDirectory, "..", "OpenLogicool.Bot"));
        var name = @"Local\OpenLogicool.Bot.Versions." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..20];
        using var mutex = new Mutex(false, name);
        try { mutex.WaitOne(); }
        catch (AbandonedMutexException) { }
        try
        {
            var version = CurrentVersion(hostDirectory);
            // 開いている間は削除できない印。導入の削除判定も同じmutex内で行う。
            return (version, new FileStream(Path.Combine(version, ".running-" + Guid.NewGuid().ToString("N")),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 1, FileOptions.DeleteOnClose));
        }
        finally { mutex.ReleaseMutex(); }
    }
}
