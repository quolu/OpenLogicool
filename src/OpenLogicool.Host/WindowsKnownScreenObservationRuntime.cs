using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Perception;

namespace OpenLogicool.Host;

public interface ILastCapturedFrameProvider
{
    CapturedFrame? LastFrame { get; }
}

/// <summary>保存済みscene profileだけで現在ページとcontrolを照合するWindows専用観測経路。</summary>
public sealed class WindowsKnownScreenObservationRuntime(
    IProductGameFrameSource frameSource,
    IWindowsGameOcrRecognizer ocr,
    ILearnedSceneProfileStore profiles,
    string gameId,
    string environmentScope) : IGameObservationRuntime, ILastCapturedFrameProvider
{
    private readonly Dictionary<string, ObservedScene> scenes = new(StringComparer.Ordinal);
    public CapturedFrame? LastFrame { get; private set; }

    public async ValueTask<ObservationResult> ObserveAsync(
        CancellationToken cancellationToken = default)
    {
        var frame = await frameSource.CaptureAsync(cancellationToken).ConfigureAwait(false);
        LastFrame = frame;
        var profile = profiles.Load(gameId, environmentScope)
            ?? throw new InvalidOperationException("既知ページ索引がまだ保存されていません。");
        var recognized = await ocr.RecognizeAsync(frame, cancellationToken).ConfigureAwait(false);
        var snapshot = new OcrFrameSnapshot(
            $"windows-ocr:{recognized.RecognizerLanguage}",
            recognized.RecognizerLanguage,
            recognized.Words.Select(word => new OcrWordBox(
                word.Text,
                word.X,
                word.Y,
                word.Width,
                word.Height)).ToArray());
        var scene = LearnedSceneMatcher.Match(
            profile,
            frame,
            snapshot);
        var refined = LearnedSceneMatcher.RefineText(profile, frame, snapshot);
        if (!ReferenceEquals(refined, profile))
        {
            profiles.Upsert(refined);
        }
        scenes[scene.ObservationId] = scene;
        return new ObservationResult(
            ContractSchemaVersions.Revision03,
            scene.ObservationId,
            scene.Frame,
            scene.CaptureAvailability,
            scene.StateIdentity,
            scene.StateCandidates,
            scene.PerceptionVersion,
            scene.Frame.FreshnessMs,
            null);
    }

    public ValueTask<ObservedScene> DiscoverTargetsAsync(
        ObservationResult observation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return scenes.TryGetValue(observation.ObservationId, out var scene)
            ? ValueTask.FromResult(scene)
            : throw new InvalidOperationException("Observeしていない既知ページObservationは解決できません。");
    }
}
