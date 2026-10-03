using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Exploration;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Host;
using OpenLogicool.Perception;
using OpenLogicool.Persistence;

namespace OpenLogicool.Probe;

/// <summary>保存原本を独立DBへ解析し直す測定。live captureと入力出力を持たない。</summary>
internal static class DemonstrationTimelineAnalysisSmoke
{
    public static int Run(string[] arguments)
    {
        string Required(string name)
        {
            var index = Array.IndexOf(arguments, name);
            if (index < 0 || index + 1 >= arguments.Length) throw new ArgumentException($"{name} が必要です。");
            return Path.GetFullPath(arguments[index + 1]);
        }
        var sourceDirectory = Required("--directory");
        var sourceDatabase = Required("--db");
        var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "probe-output",
            $"demonstration-timeline-analysis-{DateTime.Now:yyyyMMdd-HHmmss-fff}"));
        Directory.CreateDirectory(output);
        var reportPath = Path.Combine(output, "report.json");
        try
        {
            using var header = JsonDocument.Parse(File.ReadAllText(Path.Combine(sourceDirectory, "timeline-session.json")));
            if (header.RootElement.GetProperty("SchemaVersion").GetString() != "0.4.0")
                throw new InvalidOperationException("測定対象の原本schemaは0.4.0だけです。");
            var original = header.RootElement.GetProperty("Session").Deserialize<DemonstrationSessionDraft>()!;
            var draft = original with { SessionId = $"diagnostic:{Guid.NewGuid():N}", RecorderVersion = "recorder-2.0.1-sampling" };
            var frames = File.ReadLines(Path.Combine(sourceDirectory, "timeline-frames.jsonl")).Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                var value = document.RootElement;
                return new DemonstrationTimelineFrame(value.GetProperty("Frame").Deserialize<CapturedFrame>()!,
                    value.GetProperty("Artifact").Deserialize<CapturedFrameArtifact>()!, value.GetProperty("ObservedUtc").GetDateTimeOffset());
            }).ToArray();
            var inputs = File.ReadLines(Path.Combine(sourceDirectory, "timeline-inputs.jsonl"))
                .Select(line => JsonSerializer.Deserialize<DemonstrationTimelineInput>(line)!).ToArray();
            using var stop = JsonDocument.Parse(File.ReadAllText(Path.Combine(sourceDirectory, "timeline-stopped.json")));
            var stoppedUtc = stop.RootElement.GetProperty("StoppedUtc").GetDateTimeOffset();
            var database = Path.Combine(output, "analysis.db");
            using var db = new SqliteConnection($"Data Source={database};Pooling=False");
            db.Open();
            using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = sourceDatabase, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                source.Open();
                source.BackupDatabase(db);
            }
            var structures = new SqliteGameStructureStore(db);
            using var foundry = new WindowsLazyFoundryControlDiscoveryProvider(new WindowsFoundryLocalRuntimeResolver().ResolvePreferredVisionModel);
            var (discovery, resource) = WindowsProductGameExplorerComposition.CreateTargetDiscovery(
                draft.GameId, draft.EnvironmentScope,
                () => structures.LoadRevision(draft.GameId, draft.EnvironmentScope).RevisionId,
                new Uri("http://127.0.0.1:1"), "lazy-not-resolved", new SqliteLearnedSceneProfileStore(db),
                includeVisualTargets: true, controlDiscoveryProvider: foundry);
            using var visionResource = resource;
            var sourceFrames = new DemonstrationRecordedFrameSource();
            var observation = new ProductGameObservationRuntime(sourceFrames,
                new LiveObservationSource(new ZeroSeedFrameStateRecognizer()), discovery,
                new LocalPngGameFrameEvidenceSink(Path.Combine(output, "frames"), new WindowsGameFramePngEncoder()));
            var analyzedFrames = 0;
            var result = DemonstrationTimelineAnalyzer.AnalyzeAsync(draft, frames, inputs, stoppedUtc,
                new SqliteDemonstrationSessionStore(db), async (frame, token) =>
                {
                    Console.WriteLine($"保存frame {frame.Frame.Sequence}を解析中（{++analyzedFrames}枚目）");
                    await sourceFrames.SelectAsync(frame, token).ConfigureAwait(false);
                    return await observation.DiscoverTargetsAsync(await observation.ObserveAsync(token).ConfigureAwait(false), token).ConfigureAwait(false);
                }, count => Console.WriteLine($"解析完了 {count} 操作")).GetAwaiter().GetResult();
            var operations = result.Events.Where(item => item.Operation is not null).Select(item => item.Operation!).ToArray();
            var passed = operations.Length == 2 && operations.All(item => item.Comparison.Judgement == GameTransitionJudgement.Moved);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new
            {
                probe = "demonstration-timeline-analysis", sourceSessionId = original.SessionId, result.Session.SessionId,
                analyzedFrames, operations = operations.Select(item => new
                {
                    item.OperationId, item.OccurredUtc, beforeSequence = item.Before.Frame.Sequence,
                    afterSequence = item.After.StableScene?.Frame.Sequence, item.After.Status,
                    item.After.StableMillisecondsObserved, item.Comparison.Judgement, item.Comparison.Reasons,
                }), SendInput = 0, ComputerUse = 0, LiveCapture = 0, CloudApi = 0, passed,
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"{(passed ? "確認済み" : "不成立")}: {string.Join('、', operations.Select(item => item.Comparison.Judgement))}");
            Console.WriteLine($"report: {reportPath}");
            return passed ? 0 : 1;
        }
        catch (Exception exception)
        {
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { probe = "demonstration-timeline-analysis", passed = false,
                error = exception.ToString(), SendInput = 0, ComputerUse = 0, LiveCapture = 0, CloudApi = 0 }, new JsonSerializerOptions { WriteIndented = true }));
            Console.Error.WriteLine(exception.Message);
            Console.WriteLine($"report: {reportPath}");
            return 1;
        }
    }
}
