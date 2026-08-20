using System.Reflection;
using OpenLogicool.Perception;
using Xunit;

namespace OpenLogicool.Conformance.Tests;

public sealed class CorpusPartitionTests
{
    [Fact]
    public void Acceptance_is_absent_from_the_training_type_and_preserved_for_evaluation()
    {
        var partition = new CorpusPartition([Artifact("development")], [Artifact("calibration")], [Artifact("acceptance")]);
        var training = partition.ForTraining();
        var acceptance = partition.ForAcceptance();
        Assert.Equal(["development", "calibration"], training.Development.Concat(training.Calibration).Select(x => x.Id));
        Assert.Equal(["acceptance"], acceptance.Artifacts.Select(x => x.Id));
        Assert.DoesNotContain(typeof(TrainingCorpus).GetProperties(BindingFlags.Public | BindingFlags.Instance), x => x.Name.Contains("Accept", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Artifact_cannot_be_reused_between_calibration_and_acceptance()
    {
        Assert.Throws<ArgumentException>(() => new CorpusPartition([], [Artifact("same")], [Artifact("same")]));
    }

    [Fact]
    public void Same_artifact_path_with_a_different_id_cannot_cross_into_acceptance()
    {
        var calibration = new CorpusArtifact("cal-042", "corpus\\nikke\\frame001.png", "experiment:nikke");
        var acceptance = new CorpusArtifact("acc-007", "CORPUS/nikke/frame001.png", "experiment:nikke");
        Assert.Throws<ArgumentException>(() => new CorpusPartition([], [calibration], [acceptance]));
    }

    private static CorpusArtifact Artifact(string id) => new(id, $"corpus/{id}.png", "experiment:gamelab");
}
