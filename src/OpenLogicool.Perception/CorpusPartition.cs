namespace OpenLogicool.Perception;

public sealed record CorpusArtifact(string Id, string RelativePath, string Provenance);

/// <summary>recognizer の開発・校正に渡せる corpus。acceptance asset を表す field は持たない。</summary>
public sealed record TrainingCorpus(IReadOnlyList<CorpusArtifact> Development, IReadOnlyList<CorpusArtifact> Calibration);

/// <summary>凍結評価専用 corpus。開発／校正 API へは渡せない。</summary>
public sealed record AcceptanceCorpus(IReadOnlyList<CorpusArtifact> Artifacts);

public sealed class CorpusPartition
{
    private readonly IReadOnlyList<CorpusArtifact> development;
    private readonly IReadOnlyList<CorpusArtifact> calibration;
    private readonly IReadOnlyList<CorpusArtifact> acceptance;

    public CorpusPartition(
        IReadOnlyList<CorpusArtifact> development,
        IReadOnlyList<CorpusArtifact> calibration,
        IReadOnlyList<CorpusArtifact> acceptance)
    {
        this.development = Validate(development, nameof(development));
        this.calibration = Validate(calibration, nameof(calibration));
        this.acceptance = Validate(acceptance, nameof(acceptance));
        var all = this.development.Concat(this.calibration).Concat(this.acceptance).Select(artifact => artifact.Id).ToArray();
        if (all.Distinct(StringComparer.Ordinal).Count() != all.Length)
            throw new ArgumentException("corpus artifact ID は partition をまたいで重複できません。");

        var paths = this.development.Concat(this.calibration).Concat(this.acceptance)
            .Select(artifact => NormalizePath(artifact.RelativePath)).ToArray();
        if (paths.Distinct(StringComparer.Ordinal).Count() != paths.Length)
            throw new ArgumentException("同じ corpus 実体を複数の partition へ登録できません。");
    }

    public TrainingCorpus ForTraining() => new(development, calibration);
    public AcceptanceCorpus ForAcceptance() => new(acceptance);

    private static IReadOnlyList<CorpusArtifact> Validate(IReadOnlyList<CorpusArtifact> artifacts, string name)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        if (artifacts.Any(artifact => string.IsNullOrWhiteSpace(artifact.Id) || string.IsNullOrWhiteSpace(artifact.RelativePath) || string.IsNullOrWhiteSpace(artifact.Provenance)))
            throw new ArgumentException("corpus artifact には ID、相対 path、出典が必要です。", name);
        return artifacts.ToArray();
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').Trim().ToUpperInvariant();
}
