using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Exploration;
using OpenLogicool.Playbooks;

namespace OpenLogicool.Host;

/// <summary>保存済み原本だけで解析する。稼働中のゲーム画面を取得する入口を持たない。</summary>
public static class DemonstrationTimelineAnalyzer
{
    public static async Task<DemonstrationSessionRecord> AnalyzeAsync(
        DemonstrationSessionDraft draft,
        IReadOnlyList<DemonstrationTimelineFrame> frames,
        IReadOnlyList<DemonstrationTimelineInput> inputs,
        DateTimeOffset stoppedUtc,
        IDemonstrationSessionStore store,
        Func<DemonstrationTimelineFrame, CancellationToken, ValueTask<ObservedScene>> analyze,
        Action<int> progress,
        CancellationToken cancellationToken = default)
    {
        var cache = new Dictionary<long, ObservedScene>();
        async ValueTask<ObservedScene> SceneAsync(long sequence)
        {
            if (cache.TryGetValue(sequence, out var cached)) return cached;
            var frame = frames.First(item => item.Frame.Sequence == sequence);
            var scene = await analyze(frame, cancellationToken).ConfigureAwait(false);
            if (scene.Frame.Sequence != sequence || scene.Frame.SourceId != draft.TargetWindowSourceId)
                throw new InvalidOperationException("解析した観測が原本のframeへ束縛されていません。");
            cache.Add(sequence, scene);
            return scene;
        }

        var runtime = new PreparedRuntime(await SceneAsync(frames[0].Frame.Sequence));
        var recorder = new DemonstrationRecorder(store, runtime,
            point => [point.X / 1_000_000d, point.Y / 1_000_000d],
            new DemonstrationRecordingGate(), new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 1_000, 10_000));
        await recorder.StartAsync(draft, cancellationToken).ConfigureAwait(false);
        var pending = new Dictionary<string, DemonstrationTimelineInput>(StringComparer.Ordinal);
        var completed = 0;
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index];
            if (input.TargetForeground is { } foreground)
            {
                if (foreground)
                {
                    // 復帰の後、最初の入力より前に得たframeを使う。
                    var fresh = frames.FirstOrDefault(item => item.ObservedUtc >= input.OccurredUtc);
                    if (fresh is not null) runtime.Current = await SceneAsync(fresh.Frame.Sequence);
                }
                else pending.Clear();
                await recorder.ObserveForegroundAsync(
                    foreground ? draft.TargetApplicationPath : null, input.OccurredUtc, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var down = input.Kind is DemonstrationInputEdgeKind.PointerDown or DemonstrationInputEdgeKind.KeyDown;
            if (down)
            {
                runtime.Current = await SceneAsync(input.FrameSequence);
                await recorder.RefreshObservationAsync(cancellationToken).ConfigureAwait(false);
                pending[input.ControlId!] = input;
            }
            var finite = input.Kind == DemonstrationInputEdgeKind.Wheel
                || input.Kind is DemonstrationInputEdgeKind.PointerUp or DemonstrationInputEdgeKind.KeyUp
                    && pending.ContainsKey(input.ControlId!);
            if (finite)
            {
                var press = input.Kind == DemonstrationInputEdgeKind.Wheel ? input : pending[input.ControlId!];
                var before = await SceneAsync(press.FrameSequence);
                if (input.Kind == DemonstrationInputEdgeKind.Wheel)
                {
                    runtime.Current = before;
                    await recorder.RefreshObservationAsync(cancellationToken).ConfigureAwait(false);
                }
                // 次の入力、focus喪失、停止で操作の因果区間を閉じる。上限は10秒。
                var boundary = inputs.Skip(index + 1).FirstOrDefault(item =>
                    item.TargetForeground == false
                    || item.Kind is DemonstrationInputEdgeKind.PointerDown or DemonstrationInputEdgeKind.KeyDown
                    || item.Kind == DemonstrationInputEdgeKind.Wheel)?.OccurredUtc ?? stoppedUtc;
                var duration = Math.Min(10_000, Math.Max(0, (long)(boundary - input.OccurredUtc).TotalMilliseconds));
                var samples = new List<(long At, ObservedScene Scene)>();
                var candidates = frames.Where(item => item.ObservedUtc > input.OccurredUtc
                    && item.ObservedUtc < boundary
                    && item.ObservedUtc <= input.OccurredUtc.AddMilliseconds(10_000)).ToArray();
                foreach (var frame in candidates)
                {
                    // 取得済みの短い安定区間を間引いて失わず、遅い変化も同じ区間で確認する。
                    samples.Add(((long)(frame.ObservedUtc - input.OccurredUtc).TotalMilliseconds,
                        await SceneAsync(frame.Frame.Sequence)));
                }
                runtime.After = await StabilityAsync(before, samples, duration, cancellationToken).ConfigureAwait(false);
                pending.Remove(input.ControlId!);
            }
            await recorder.HandleAsync(ToEdge(input), cancellationToken).ConfigureAwait(false);
            if (finite) progress(++completed);
        }
        return recorder.StopAsync("利用者が記録を停止しました。原本の解析を完了しました。", stoppedUtc);
    }

    private static DemonstrationInputEdge ToEdge(DemonstrationTimelineInput input) => new(
        ContractSchemaVersions.Revision03, input.Source!.Value, input.Kind!.Value, input.ControlId!, input.OutputToken!,
        input.MonotonicMs, input.OccurredUtc,
        input.NormalizedPoint is { } point
            ? new DemonstrationScreenPoint((int)Math.Round(point[0] * 1_000_000), (int)Math.Round(point[1] * 1_000_000)) : null,
        input.WheelVerticalSteps, input.WheelHorizontalSteps);

    private static async ValueTask<GameInteractionStabilityResult> StabilityAsync(
        ObservedScene before, IReadOnlyList<(long At, ObservedScene Scene)> samples, long duration, CancellationToken cancellationToken)
    {
        if (duration == 0 || samples.Count == 0)
            return new(ContractSchemaVersions.Revision03, GameInteractionStabilityStatus.TimedOut, [], null, 0, 0, duration,
                "次の入力または停止までの区間に、操作後の画面が保存されていません。");
        var replay = new RecordedSceneRuntime(samples, duration);
        // AIの処理時間でなく、原本の取得時刻を使って既存の安定判定を通す。
        return await new GameInteractionStabilityRuntime(replay, replay, TimeSpan.FromMilliseconds(100))
            .WaitStableAsync(before, new ExplorationWaitCondition(ContractSchemaVersions.Revision03, 2, 1_000, (int)duration),
                cancellationToken).ConfigureAwait(false);
    }

    private sealed class PreparedRuntime(ObservedScene scene) : IDemonstrationObservationRuntime
    {
        public ObservedScene Current { get; set; } = scene;
        public GameInteractionStabilityResult After { get; set; } = null!;
        public ValueTask<ObservationResult> ObserveAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Observation(Current));
        public ValueTask<ObservedScene> DiscoverTargetsAsync(ObservationResult observation, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Current);
        public ValueTask<GameInteractionStabilityResult> WaitStableAsync(
            ObservedScene before, ExplorationWaitCondition condition, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(After);
        public GameTransitionComparison Compare(ObservedScene before, GameInteractionStabilityResult after) =>
            new GameTransitionJudge().Compare(before, after);
    }

    private sealed class RecordedSceneRuntime(IReadOnlyList<(long At, ObservedScene Scene)> samples, long duration)
        : IGameObservationRuntime, IGameInteractionClock
    {
        private ObservedScene current = samples[0].Scene;
        public long ElapsedMilliseconds { get; private set; }
        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = samples.FirstOrDefault(item => item.At >= ElapsedMilliseconds + (long)delay.TotalMilliseconds);
            if (next.Scene is null) ElapsedMilliseconds = duration;
            else { ElapsedMilliseconds = next.At; current = next.Scene; }
            return ValueTask.CompletedTask;
        }
        public ValueTask<ObservationResult> ObserveAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Observation(current));
        public ValueTask<ObservedScene> DiscoverTargetsAsync(ObservationResult observation, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(current);
    }

    private static ObservationResult Observation(ObservedScene scene) => new(
        ContractSchemaVersions.Revision03, scene.ObservationId, scene.Frame, scene.CaptureAvailability,
        scene.StateIdentity, scene.StateCandidates, "recorded-demonstration", 0, null);
}
