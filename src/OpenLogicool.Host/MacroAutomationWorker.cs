using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Input;

namespace OpenLogicool.Host;

/// <summary>G13/G600 fast pathの有界queueをproduct macro runnerへ渡す専用worker。</summary>
public sealed class MacroAutomationWorker : IDisposable
{
    private readonly MacroInvocationQueue queue;
    private readonly IMacroInvocationRunner runner;
    private readonly Action<MacroRunSnapshot>? observer;
    private readonly ManualResetEvent stop = new(false);
    private readonly CancellationTokenSource cancellation = new();
    private readonly Thread thread;
    private bool started;
    private bool disposed;

    public MacroAutomationWorker(
        MacroInvocationQueue queue,
        IMacroInvocationRunner runner,
        Action<MacroRunSnapshot>? observer = null)
    {
        this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
        this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
        this.observer = observer;
        thread = new Thread(Work) { IsBackground = true, Name = "OpenLogicoolMacroAutomation" };
    }

    public Exception? LastFailure { get; private set; }

    public void Start()
    {
        if (started || disposed) throw new InvalidOperationException("macro workerは一度しか開始できません。");
        started = true;
        thread.Start();
    }

    private void Work()
    {
        var handles = new[] { stop, queue.Available };
        while (!stop.WaitOne(0))
        {
            WaitHandle.WaitAny(handles);
            while (!stop.WaitOne(0) && queue.TryDequeue(out var invocation))
            {
                try
                {
                    var progress = new InlineProgress(snapshot => observer?.Invoke(snapshot));
                    _ = runner.RunQueuedAsync(invocation, progress, cancellation.Token).GetAwaiter().GetResult();
                    LastFailure = null;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception error)
                {
                    LastFailure = error;
                }
            }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        cancellation.Cancel();
        stop.Set();
        if (started && thread.IsAlive && !thread.Join(TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException("macro workerが5秒以内に停止しませんでした。");
        cancellation.Dispose();
        stop.Dispose();
    }

    private sealed class InlineProgress(Action<MacroRunSnapshot> report) : IProgress<MacroRunSnapshot>
    {
        public void Report(MacroRunSnapshot value) => report(value);
    }
}
