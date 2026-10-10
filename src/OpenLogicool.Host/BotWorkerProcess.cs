using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace OpenLogicool.Host;

internal sealed record BotWorkerChild(
    TextWriter StandardInput, TextReader StandardOutput, TextReader StandardError,
    Task Exited, Func<int> ExitCode, Action AttachJob, Action CloseJob, Action Kill, int ProcessId);

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
        return await RunAsync(run, server, new(child.StandardInput, child.StandardOutput, child.StandardError,
            child.WaitForExitAsync(), () => child.ExitCode, () => job.Add(child), job.Dispose,
            () => { if (!child.HasExited) child.Kill(entireProcessTree: true); }, child.Id), token);
    }

    // 起動済みの子の入出力と終了通知だけを受ける。本番とにせの子は同じ終了処理を通る。
    internal static async Task<BotScriptResult> RunAsync(BotWorkerRun run, BotWorkerPipeServer server,
        BotWorkerChild child, CancellationToken token)
    {
        var stdout = DrainAsync(child.StandardOutput, Path.Combine(run.EvidenceDirectory, "worker-stdout.log"));
        var stderr = DrainAsync(child.StandardError, Path.Combine(run.EvidenceDirectory, "worker-stderr.log"));
        try
        {
            child.AttachJob();
            await child.StandardInput.WriteLineAsync("start");
            await child.StandardInput.FlushAsync();
            using var registration = token.Register(server.Stop);
            await Task.WhenAny(child.Exited, server.Stopped, stdout, stderr);
        }
        finally
        {
            server.Stop();
            child.StandardInput.Close();
            try { await FinishChildAsync(child, run.EvidenceDirectory); }
            finally { child.CloseJob(); }
            // Jobを先に閉じ、標準出力を受け継いだ孫も終了させてから排出を待つ。
            // 子の終了では番を空けない。受付済み入力も回収してから呼出元へ戻す。
            await server.Completion;
            await Task.WhenAll(stdout, stderr);
        }
        token.ThrowIfCancellationRequested();
        if (server.Fault is { } fault) throw new IOException("Botとの通信が終了しました: " + fault.Message, fault);
        var exitCode = child.ExitCode();
        if (exitCode != 0)
        {
            var error = File.ReadAllText(Path.Combine(run.EvidenceDirectory, "worker-stderr.log")).Trim();
            if (error.Length > 4096) error = "（標準エラーの末尾4096文字）\n" + error[^4096..];
            throw new IOException($"Botが異常終了しました（終了コード {exitCode}）。{run.EvidenceDirectory}"
                + (error.Length == 0 ? "" : "\n" + error));
        }
        var json = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(run.EvidenceDirectory, "result.json")));
        return new(json.TryGetProperty("NeedsReview", out var review) && review.GetBoolean(),
            json.TryGetProperty("Detail", out var detail) ? detail.GetString()! : "実行が終了しました。");
    }

    private static async Task FinishChildAsync(BotWorkerChild child, string evidence)
    {
        if (await Task.WhenAny(child.Exited, Task.Delay(StopDeadline)) != child.Exited)
        {
            File.WriteAllText(Path.Combine(evidence, "worker-stop-timeout.json"), JsonSerializer.Serialize(new
            {
                Reason = "停止の合図から5秒以内にBotが終了しなかったため、子processを終了します。",
                child.ProcessId, DeadlineMilliseconds = StopDeadline.TotalMilliseconds,
            }));
            child.Kill();
        }
        await child.Exited;
    }

    private static async Task DrainAsync(TextReader reader, string path)
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
