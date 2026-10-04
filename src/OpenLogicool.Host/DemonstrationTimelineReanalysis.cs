using System.IO;
using System.Text.Json;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Perception;

namespace OpenLogicool.Host;

public interface IDemonstrationTimelineReanalysis
{
    Task<DemonstrationSessionRecord> AnalyzeAsync(
        DemonstrationSessionRecord source, IDemonstrationSessionStore store,
        Action<int> progress, CancellationToken cancellationToken);
}

/// <summary>保存原本から別の解析結果を作る。ゲームの取得・入力と原本の更新は行わない。</summary>
public sealed class WindowsDemonstrationTimelineReanalysis(string databasePath) : IDemonstrationTimelineReanalysis
{
    public Task<DemonstrationSessionRecord> AnalyzeAsync(
        DemonstrationSessionRecord source, IDemonstrationSessionStore store,
        Action<int> progress, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        var archive = DemonstrationTimelineArchive.Load(source);
        var connections = new MacroSqliteConnectionFactory(databasePath);
        var structures = new MacroGameStructureStore(connections);
        using var foundry = new WindowsLazyFoundryControlDiscoveryProvider(new WindowsFoundryLocalRuntimeResolver().ResolvePreferredVisionModel);
        var (discovery, resource) = WindowsProductGameExplorerComposition.CreateTargetDiscovery(
            source.Session.GameId, source.Session.EnvironmentScope,
            () => structures.LoadRevision(source.Session.GameId, source.Session.EnvironmentScope).RevisionId,
            new Uri("http://127.0.0.1:1"), "lazy-not-resolved", new MacroLearnedSceneProfileStore(connections),
            includeVisualTargets: true, controlDiscoveryProvider: foundry);
        using var visionResource = resource;
        var frames = new DemonstrationRecordedFrameSource();
        var output = Path.Combine(Path.GetDirectoryName(archive.Frames[0].Artifact.LocalPath!)!,
            "analysis", Guid.NewGuid().ToString("N"));
        var observation = new ProductGameObservationRuntime(frames,
            new LiveObservationSource(new ZeroSeedFrameStateRecognizer()), discovery,
            new LocalPngGameFrameEvidenceSink(output, new WindowsGameFramePngEncoder()));
        return await archive.AnalyzeAsync(store, async (frame, token) =>
        {
            await frames.SelectAsync(frame, token).ConfigureAwait(false);
            return await observation.DiscoverTargetsAsync(await observation.ObserveAsync(token).ConfigureAwait(false), token).ConfigureAwait(false);
        }, progress, cancellationToken).ConfigureAwait(false);
    }, cancellationToken);
}

public sealed record DemonstrationTimelineArchive(
    DemonstrationSessionDraft Source,
    IReadOnlyList<DemonstrationTimelineFrame> Frames,
    IReadOnlyList<DemonstrationTimelineInput> Inputs,
    DateTimeOffset StoppedUtc)
{
    public static DemonstrationTimelineArchive Load(DemonstrationSessionRecord source)
    {
        if (source.State != DemonstrationSessionState.Stopped)
            throw new InvalidOperationException("記録を終了してから解析し直してください。");
        var image = source.Events.FirstOrDefault(item => item.Operation is not null)?.Operation?.Before.Frame.Artifact?.LocalPath
            ?? throw new InvalidOperationException("この記録には再解析できる画像原本がありません。");
        var directory = Path.GetDirectoryName(image)!;
        using var header = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "timeline-session.json")));
        if (header.RootElement.GetProperty("SchemaVersion").GetString() != "0.4.0")
            throw new InvalidOperationException("再解析できる記録原本は形式0.4.0だけです。");
        var original = header.RootElement.GetProperty("Session").Deserialize<DemonstrationSessionDraft>()!;
        if (original != source.Session)
            throw new InvalidOperationException("画像原本の記録と選択したデモが一致しません。");
        var frames = File.ReadLines(Path.Combine(directory, "timeline-frames.jsonl"))
            .Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                var value = document.RootElement;
                return new DemonstrationTimelineFrame(value.GetProperty("Frame").Deserialize<CapturedFrame>()!,
                    value.GetProperty("Artifact").Deserialize<CapturedFrameArtifact>()!, value.GetProperty("ObservedUtc").GetDateTimeOffset());
            }).ToArray();
        var inputs = File.ReadLines(Path.Combine(directory, "timeline-inputs.jsonl"))
            .Select(line => JsonSerializer.Deserialize<DemonstrationTimelineInput>(line)!).ToArray();
        using var stop = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "timeline-stopped.json")));
        if (stop.RootElement.GetProperty("SchemaVersion").GetString() != "0.4.0" || frames.Length == 0)
            throw new InvalidOperationException("記録原本の停止情報または画像一覧が不正です。");
        return new(original, frames, inputs, stop.RootElement.GetProperty("StoppedUtc").GetDateTimeOffset());
    }

    public Task<DemonstrationSessionRecord> AnalyzeAsync(
        IDemonstrationSessionStore store,
        Func<DemonstrationTimelineFrame, CancellationToken, ValueTask<ObservedScene>> analyze,
        Action<int> progress, CancellationToken cancellationToken = default) =>
        DemonstrationTimelineAnalyzer.AnalyzeAsync(Source with
        {
            SessionId = $"demo-analysis:{Guid.NewGuid():N}",
            SourceSessionId = Source.SessionId,
            ReanalyzedUtc = DateTimeOffset.UtcNow,
            RecorderVersion = "recorder-2.1.0-reanalysis",
        }, Frames, Inputs, StoppedUtc, store, analyze, progress, cancellationToken);
}
