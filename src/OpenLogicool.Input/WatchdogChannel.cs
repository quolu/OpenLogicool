using System.Diagnostics;
using OpenLogicool.Contracts.Devices.Shared;

namespace OpenLogicool.Input;

/// <summary>
/// watchdog process との接続（計画 §6.2・DEV-009: hard crash 後の期限内 release は watchdog が条件）。
/// 行 protocol を stdin で送る: "DOWN KEY 7C"／"UP KEY 7C"／"DOWN MOUSE Left"／"UP MOUSE Left"／"EXIT"。
/// host が hard crash すると stdin pipe が閉じ、watchdog は EOF を検出して追跡中の全 output を release する。
/// 通常終了は Shutdown（EXIT 送信）で watchdog に「release 不要」を伝える。
/// </summary>
public sealed class WatchdogChannel : IDisposable
{
    private readonly Process _process;
    private readonly StreamWriter _stdin;

    private WatchdogChannel(Process process)
    {
        _process = process;
        _stdin = process.StandardInput;
    }

    public static WatchdogChannel Start(string watchdogExePath)
    {
        if (!File.Exists(watchdogExePath))
        {
            throw new FileNotFoundException("watchdog 実行ファイルが見つかりません。", watchdogExePath);
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = watchdogExePath,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"watchdog を起動できませんでした: {watchdogExePath}");
        }

        return new WatchdogChannel(process);
    }

    public bool HasExited => _process.HasExited;

    public void Notify(string outputToken, PhysicalInputEdge edge)
    {
        var line = EncodeLine(outputToken, edge);
        _stdin.WriteLine(line);
        _stdin.Flush();
    }

    /// <summary>通常終了。watchdog は追跡中 output を release せずに終了する（host 側が release 済みの前提）。</summary>
    public void Shutdown()
    {
        _stdin.WriteLine("EXIT");
        _stdin.Flush();
        if (!_process.WaitForExit(2000))
        {
            throw new InvalidOperationException("watchdog が EXIT 後 2 秒以内に終了しませんでした。");
        }
    }

    public static string EncodeLine(string outputToken, PhysicalInputEdge edge)
    {
        var resolved = OutputTokens.Parse(outputToken);
        var verb = edge == PhysicalInputEdge.Down ? "DOWN" : "UP";
        return resolved.Kind == ResolvedOutputKind.Key
            ? $"{verb} KEY {resolved.VirtualKey:X2}{(resolved.IsExtendedKey ? " EXT" : "")}"
            : $"{verb} MOUSE {resolved.MouseButton}";
    }

    public void Dispose()
    {
        _stdin.Dispose();
        _process.Dispose();
    }
}

/// <summary>
/// watchdog 追跡付きの emitter。down は「先に watchdog へ通知 → 送出」、up は「送出 → 通知」の順で行い、
/// どの瞬間に hard crash しても『watchdog が知らない down』が存在しないことを保証する
/// （通知済み・未送出の down を watchdog が release しても、押されていない key への key-up は無害）。
/// </summary>
public sealed class GuardedOutputEmitter(IOutputEmitter inner, WatchdogChannel watchdog) : IOutputEmitter
{
    public void Emit(IReadOnlyList<MappedOutputEdge> edges)
    {
        foreach (var edge in edges)
        {
            if (edge.Edge == PhysicalInputEdge.Down)
            {
                watchdog.Notify(edge.Output, PhysicalInputEdge.Down);
            }
        }

        inner.Emit(edges);

        foreach (var edge in edges)
        {
            if (edge.Edge == PhysicalInputEdge.Up)
            {
                watchdog.Notify(edge.Output, PhysicalInputEdge.Up);
            }
        }
    }
}
