namespace OpenLogicool.Host;

/// <summary>進行処理のOCRと結果待ちから回復監視を分離し、終了時は両方を回収する。</summary>
internal static class VisualKeyAssistWorkers
{
    public static async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> recovery,
        Func<CancellationToken, Task<T>> progress, CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var recoveryTask = Task.Run(() => recovery(stop.Token), stop.Token);
        var progressTask = Task.Run(() => progress(stop.Token), stop.Token);
        var completed = await Task.WhenAny(recoveryTask, progressTask);
        try { return await completed; }
        finally
        {
            await stop.CancelAsync();
            var other = completed == recoveryTask ? progressTask : recoveryTask;
            try { await other; }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }
}
