using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Perception;

namespace OpenLogicool.Host;

/// <summary>保存済みscene profileだけで現在ページとcontrolを照合するWindows専用観測経路。</summary>
public sealed class WindowsKnownScreenObservationRuntime(
    IProductGameFrameSource frameSource,
    IWindowsGameOcrRecognizer ocr,
    ILearnedSceneProfileStore profiles,
    string gameId,
    string environmentScope) : IGameObservationRuntime
{
    private readonly Dictionary<string, ObservedScene> scenes = new(StringComparer.Ordinal);

    public async ValueTask<ObservationResult> ObserveAsync(
        CancellationToken cancellationToken = default)
    {
        var frame = await frameSource.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var profile = profiles.Load(gameId, environmentScope)
            ?? throw new InvalidOperationException("既知ページ索引がまだ保存されていません。");
        var recognized = await ocr.RecognizeAsync(frame, cancellationToken).ConfigureAwait(false);
        var scene = LearnedSceneMatcher.Match(
            profile,
            frame,
            new OcrFrameSnapshot(
                $"windows-ocr:{recognized.RecognizerLanguage}",
                recognized.RecognizerLanguage,
                recognized.Words.Select(word => new OcrWordBox(
                    word.Text,
                    word.X,
                    word.Y,
                    word.Width,
                    word.Height)).ToArray()));
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
