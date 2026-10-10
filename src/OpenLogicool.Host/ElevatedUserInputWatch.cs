using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace OpenLogicool.Host;

/// <summary>
/// 管理者権限の監視processが観測したキーボードとマウスの手入力を受け取る。
/// 対象のゲームがBotより高い権限で動く時、Botのprocessからはその入力が見えないため、観測だけを監視processへ分ける。
/// 連絡は監視processからの一方通行で、Bot本体からは何も送らない。監視が止まったら、見えないまま続けずに失敗にする。
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

    public ElevatedUserInputWatch(int requiredIntegrity, Action? launch = null,
        string pipeName = UserInputWatchProtocol.PipeName, int startTimeoutMs = 15_000)
    {
        pipe = new(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            (launch ?? RunRegisteredTask)();
            using var timeout = new CancellationTokenSource(startTimeoutMs);
            try
            {
                pipe.WaitForConnectionAsync(timeout.Token).GetAwaiter().GetResult();
                reader = new(pipe, new UTF8Encoding(false), false, 256, leaveOpen: true);
                var integrity = UserInputWatchProtocol.ParseReady(reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult());
                if (integrity < requiredIntegrity)
                    throw new InvalidOperationException(FormattableString.Invariant(
                        $"手入力の監視processの権限が足りません（監視=0x{integrity:X} 必要=0x{requiredIntegrity:X}）。scripts/install-user-input-watch.ps1 で導入し直してください。"));
                latest = UserInputWatchProtocol.Parse(reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult()
                    ?? throw new InvalidOperationException("手入力の監視processが報告の前に終了しました。"));
                latestAt = clock.ElapsedMilliseconds;
            }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException("管理者権限の手入力の監視processが時間内に始まりません。"
                    + "scripts/install-user-input-watch.ps1 で導入し直してください。");
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
                + "scripts/install-user-input-watch.ps1 を実行して導入してください。");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        pipe.Dispose(); // 監視processは、連絡が切れたら終了する。
    }
}
