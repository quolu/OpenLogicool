using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace OpenLogicool.Host;

/// <summary>
/// 管理者権限の監視processとの連絡。一行ずつ送る。押下の数と無入力の時間だけを渡し、どのキーかは渡さない。
/// </summary>
internal static class UserInputWatchProtocol
{
    public const string PipeName = "OpenLogicool.UserInputWatch";
    /// <summary>監視processを管理者権限で起動する、登録済みのタスク（scripts/register-user-input-watch.ps1）。</summary>
    public const string TaskName = @"\OpenLogicool\UserInputWatch";
    public const int ReportIntervalMs = 50;

    public static string Ready(int integrity) => FormattableString.Invariant($"ready {integrity}");

    public static int ParseReady(string? line) =>
        line?.Split(' ') is ["ready", var level] && int.TryParse(level, NumberStyles.None, CultureInfo.InvariantCulture, out var integrity)
            ? integrity : throw new FormatException($"監視processの開始の合図を読めません: {line}");

    public static string Format(UserInputPauseSnapshot snapshot) => FormattableString.Invariant(
        $"{snapshot.HeldCount} {snapshot.IdleMilliseconds} {snapshot.UserEvents} {snapshot.NanoEvents} {snapshot.LostReleases}");

    public static UserInputPauseSnapshot Parse(string line)
    {
        var parts = line.Split(' ');
        if (parts.Length != 5 || parts.Any(part => !long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            throw new FormatException($"監視processの報告を読めません: {line}");
        var values = parts.Select(part => long.Parse(part, CultureInfo.InvariantCulture)).ToArray();
        return new(values[0] > 0 || values[1] < UserInputPauseState.QuietMilliseconds, (int)values[0], values[1], values[2], values[3],
            values[4], []);
    }
}

/// <summary>
/// 管理者権限の監視processが観測したキーボードとマウスの手入力を受け取る。
/// 対象のゲームがBotより高い権限で動く時、Botのprocessからはその入力が見えないため、観測だけを監視processへ分ける。
/// 監視が止まったら、見えないまま続けずに失敗にする。
/// </summary>
internal sealed class ElevatedUserInputWatch : IRawUserInputSource
{
    // 監視processは50msごとに報告する。これだけ途絶えたら、止まったとみなす。
    private const int StaleMilliseconds = 2000;
    private readonly NamedPipeServerStream pipe;
    private readonly StreamReader reader;
    private readonly Thread thread;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Lock gate = new();
    private UserInputPauseSnapshot latest;
    private long latestAt;
    private volatile Exception? failure;
    private volatile bool disposed;

    public ElevatedUserInputWatch(SerialHidCandidate nano, int requiredIntegrity, Action? launch = null,
        string pipeName = UserInputWatchProtocol.PipeName, int startTimeoutMs = 15_000)
    {
        pipe = new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            (launch ?? RunRegisteredTask)();
            using var timeout = new CancellationTokenSource(startTimeoutMs);
            try
            {
                pipe.WaitForConnectionAsync(timeout.Token).GetAwaiter().GetResult();
                var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                writer.WriteLine(JsonSerializer.Serialize(nano));
                reader = new(pipe, new UTF8Encoding(false), false, 256, leaveOpen: true);
                var integrity = UserInputWatchProtocol.ParseReady(reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult());
                if (integrity < requiredIntegrity)
                    throw new InvalidOperationException(FormattableString.Invariant(
                        $"手入力の監視processの権限が足りません（監視=0x{integrity:X} 必要=0x{requiredIntegrity:X}）。管理者権限のタスクとして登録し直してください。"));
                latest = UserInputWatchProtocol.Parse(reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult()
                    ?? throw new InvalidOperationException("手入力の監視processが報告の前に終了しました。"));
                latestAt = clock.ElapsedMilliseconds;
            }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException("管理者権限の手入力の監視processが時間内に始まりません。"
                    + "scripts/register-user-input-watch.ps1 の登録と、導入先のOpenLogicool.Launcher.exeを確かめてください。");
            }
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
        thread = new Thread(Read) { IsBackground = true, Name = "手入力の監視processの報告" };
        thread.Start();
    }

    public UserInputPauseSnapshot Snapshot()
    {
        if (failure is { } error) throw new InvalidOperationException("管理者権限の手入力の監視に失敗しました。", error);
        lock (gate)
        {
            var age = clock.ElapsedMilliseconds - latestAt;
            if (age > StaleMilliseconds)
                throw new InvalidOperationException($"管理者権限の手入力の監視から{age}ms報告がありません。手入力が見えないため続けません。");
            var idle = latest.IdleMilliseconds + age;
            return latest with { IdleMilliseconds = idle, Paused = latest.HeldCount > 0 || idle < UserInputPauseState.QuietMilliseconds };
        }
    }

    private void Read()
    {
        try
        {
            while (reader.ReadLine() is { } line)
            {
                var snapshot = UserInputWatchProtocol.Parse(line);
                lock (gate) { latest = snapshot; latestAt = clock.ElapsedMilliseconds; }
            }
            if (!disposed) failure = new IOException("手入力の監視processが終了しました。");
        }
        catch (Exception error) when (!disposed) { failure = error; }
        catch (Exception) { }
    }

    private static void RunRegisteredTask()
    {
        using var process = Process.Start(new ProcessStartInfo("schtasks.exe")
        {
            ArgumentList = { "/Run", "/TN", UserInputWatchProtocol.TaskName },
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("schtasks.exe を起動できません。");
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"管理者権限の手入力の監視を起動できません（schtasks exit={process.ExitCode}）。"
                + "ゲームが管理者権限で動いているため、手入力の監視にも管理者権限が要ります。"
                + "scripts/register-user-input-watch.ps1 を1回実行して登録してください。");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        pipe.Dispose(); // 監視processは、連絡が切れたら終了する。
    }
}

/// <summary>管理者権限のタスクから起動される監視process。キーボードとマウスの手入力を観測し、Botへ報告する。</summary>
internal static class HostUserInputWatch
{
    public static int Run(string[] arguments)
    {
        try
        {
            var index = Array.IndexOf(arguments, "--pipe");
            using var pipe = new NamedPipeClientStream(".", index >= 0 && index + 1 < arguments.Length ? arguments[index + 1]
                : UserInputWatchProtocol.PipeName, PipeDirection.InOut, PipeOptions.None);
            pipe.Connect(10_000);
            // 入力の様子は、同じ場所のOpenLogicool.Hostにだけ渡す。同じ名前の通信口を別のprocessが開いていたら渡さない。
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverId)) throw new Win32Exception(Marshal.GetLastWin32Error());
            using (var server = Process.GetProcessById((int)serverId))
                if (!string.Equals(server.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine("通信口の相手が同じ場所のOpenLogicool.Hostではないため、終了します。");
                    return 3;
                }
            var encoding = new UTF8Encoding(false);
            var reader = new StreamReader(pipe, encoding, false, 256, leaveOpen: true);
            var writer = new StreamWriter(pipe, encoding, 256, leaveOpen: true) { AutoFlush = true };
            var nano = JsonSerializer.Deserialize<SerialHidCandidate>(reader.ReadLine()
                ?? throw new InvalidOperationException("Nanoの識別を受け取る前に連絡が切れました。"))
                ?? throw new InvalidOperationException("Nanoの識別が空です。");
            using var monitor = new WindowsUserInputMonitor(nano);
            writer.WriteLine(UserInputWatchProtocol.Ready(ProcessIntegrity.Current()));
            while (true)
            {
                writer.WriteLine(UserInputWatchProtocol.Format(monitor.Snapshot()));
                Thread.Sleep(UserInputWatchProtocol.ReportIntervalMs);
            }
        }
        catch (IOException) { return 0; } // Botが連絡を閉じた。
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 2;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
}
