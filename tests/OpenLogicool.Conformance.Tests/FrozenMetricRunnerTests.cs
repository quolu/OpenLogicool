using System.Reflection;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Perception;
using Xunit;

namespace OpenLogicool.Conformance.Tests;

public sealed class FrozenMetricRunnerTests
{
    [Fact]
    public void Acceptance_only_evaluation_passes_the_fixed_zero_thresholds()
    {
        var artifact = Artifact("known");
        var report = FrozenMetricRunner.Evaluate(new AcceptanceCorpus([artifact]),
            [new FrozenMetricCase(artifact, ObservationStatus.Known, true, ObservationStatus.Known, true)]);

        Assert.True(report.Passed);
        Assert.Equal(0, report.KnownMisclassifications + report.UnknownPromotions + report.SuccessFalsePositives);
        Assert.DoesNotContain(typeof(FrozenMetricRunner).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .SelectMany(method => method.GetParameters()), parameter => parameter.ParameterType == typeof(TrainingCorpus));
    }

    [Fact]
    public void Fixed_metrics_count_every_prohibited_promotion_and_dispatch()
    {
        var unknown = Artifact("unknown");
        var ambiguous = Artifact("ambiguous");
        var noResume = Artifact("no-resume");
        var report = FrozenMetricRunner.Evaluate(new AcceptanceCorpus([unknown, ambiguous, noResume]),
        [
            new FrozenMetricCase(unknown, ObservationStatus.Unknown, false, ObservationStatus.Known, true),
            new FrozenMetricCase(ambiguous, ObservationStatus.Ambiguous, false, ObservationStatus.Known, false),
            new FrozenMetricCase(noResume, ObservationStatus.Known, false, ObservationStatus.Known, true),
        ]);

        Assert.False(report.Passed);
        Assert.Equal(2, report.KnownMisclassifications);
        Assert.Equal(1, report.UnknownPromotions);
        Assert.Equal(2, report.SuccessFalsePositives);
    }

    [Fact]
    public void Cases_must_cover_only_the_frozen_acceptance_artifacts()
    {
        var accepted = Artifact("accepted");
        var other = Artifact("other");

        Assert.Throws<ArgumentException>(() => FrozenMetricRunner.Evaluate(new AcceptanceCorpus([accepted]),
            [new FrozenMetricCase(other, ObservationStatus.Known, true, ObservationStatus.Known, true)]));
    }

    private static CorpusArtifact Artifact(string id) => new(id, $"acceptance/{id}.png", "fixture:phase5");
}
