using System.Collections.Concurrent;
using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Input;

public enum MacroInvocationEnqueueResult
{
    Accepted,
    Busy,
    QueueFull,
    Unavailable,
}

/// <summary>
/// fast pathからmacro workerへ待機なしで起動要求を渡す境界。
/// 実装はAI・capture・SQLite・UIへ到達せず、即座に結果を返す。
/// </summary>
public interface IMacroInvocationSink
{
    MacroInvocationEnqueueResult TryEnqueue(MacroVersionReference invocation);
}

/// <summary>一つの実行中macroと有界待ち列を所有する、process内の非blocking queue。</summary>
public sealed class MacroInvocationQueue : IMacroInvocationSink, IDisposable
{
    private readonly ConcurrentQueue<MacroVersionReference> queue = new();
    private readonly AutoResetEvent available = new(false);
    private readonly int capacity;
    private int count;
    private volatile bool disposed;

    public MacroInvocationQueue(int capacity = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        this.capacity = capacity;
    }

    public WaitHandle Available => available;

    public int Count => Volatile.Read(ref count);

    public MacroInvocationEnqueueResult TryEnqueue(MacroVersionReference invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (disposed)
        {
            return MacroInvocationEnqueueResult.Unavailable;
        }

        while (true)
        {
            var observed = Volatile.Read(ref count);
            if (observed >= capacity)
            {
                return MacroInvocationEnqueueResult.QueueFull;
            }
            if (Interlocked.CompareExchange(ref count, observed + 1, observed) == observed)
            {
                break;
            }
        }

        queue.Enqueue(invocation);
        available.Set();
        return MacroInvocationEnqueueResult.Accepted;
    }

    public bool TryDequeue(out MacroVersionReference invocation)
    {
        if (queue.TryDequeue(out invocation!))
        {
            Interlocked.Decrement(ref count);
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        available.Set();
        available.Dispose();
    }
}
