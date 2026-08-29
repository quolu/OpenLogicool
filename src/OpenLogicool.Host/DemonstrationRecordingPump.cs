using System.Threading.Channels;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Playbooks;

namespace OpenLogicool.Host;

/// <summary>
/// OS hook procedureから同期・非blockingで届く入力edgeを、記録器の非同期処理（WaitStable等の
/// 待ち合わせを含む）へ順序どおり橋渡しする。Observeは即returnし、処理は専用loopが1件ずつ
/// HandleAsyncを呼ぶ——hook threadを記録処理の待ち時間で塞がない。
/// </summary>
public sealed class DemonstrationRecordingPump : IDemonstrationInputSink, IDisposable
{
    private readonly DemonstrationRecorder recorder;
    private readonly Channel<DemonstrationInputEdge> channel = Channel.CreateUnbounded<DemonstrationInputEdge>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task pumpTask;

    public DemonstrationRecordingPump(DemonstrationRecorder recorder)
    {
        this.recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        pumpTask = Task.Run(PumpAsync);
    }

    public long DroppedCount { get; private set; }

    public void Observe(DemonstrationInputEdge edge)
    {
        if (!channel.Writer.TryWrite(edge))
        {
            DroppedCount++;
        }
    }

    private async Task PumpAsync()
    {
        try
        {
            await foreach (var edge in channel.Reader.ReadAllAsync(cancellation.Token).ConfigureAwait(false))
            {
                await recorder.HandleAsync(edge, cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>受理済みの残りedgeを処理し終えてから停止する。</summary>
    public async Task DrainAndStopAsync()
    {
        channel.Writer.TryComplete();
        await pumpTask.ConfigureAwait(false);
    }

    public void Dispose()
    {
        channel.Writer.TryComplete();
        cancellation.Cancel();
        try
        {
            pumpTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
        }

        cancellation.Dispose();
    }
}
