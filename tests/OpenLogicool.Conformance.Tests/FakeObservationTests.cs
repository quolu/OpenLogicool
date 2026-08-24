using System.Reflection;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Fakes;
using Xunit;

namespace OpenLogicool.Conformance.Tests;

public sealed class FakeObservationTests
{
    private static CapturedFrame AnyFrame() =>
        new(
            "0.1.0",
            "fake-source",
            CaptureBackend.WindowsGraphicsCapture,
            Sequence: 1,
            MonotonicMs: 1.0,
            WallClockUtc: new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero),
            Width: 1920,
            Height: 1080,
            PixelFormat: "BGRA8",
            DpiX: 96.0,
            DpiY: 96.0,
            TransformRevision: 1,
            FreshnessMs: 16,
            LastChangeMs: 0);

    [Fact]
    public void Capture_and_identity_axes_conform_to_the_observation_contract()
    {
        var observations = new[]
        {
            FakeObservations.Known("observation-known", "state-main-menu"),
            FakeObservations.Ambiguous("observation-ambiguous", "state-main-menu", "state-rewards"),
            FakeObservations.Unknown("observation-unknown"),
            FakeObservations.Novel("observation-novel"),
            FakeObservations.Unavailable("observation-unavailable", "capture-black"),
            FakeObservations.Stale("observation-stale", "freshness-budget"),
        };

        foreach (var observation in observations)
        {
            ContractConformanceSuite.Verify(observation);
        }

        Assert.Equal(
            [CaptureAvailability.Available, CaptureAvailability.Available, CaptureAvailability.Available,
                CaptureAvailability.Available, CaptureAvailability.Unavailable, CaptureAvailability.Stale],
            observations.Select(observation => observation.CaptureAvailability));
        Assert.Equal(
            [StateIdentityStatus.Known, StateIdentityStatus.Ambiguous, StateIdentityStatus.InsufficientEvidence,
                StateIdentityStatus.Novel, StateIdentityStatus.InsufficientEvidence, StateIdentityStatus.InsufficientEvidence],
            observations.Select(observation => observation.StateIdentity));
    }

    [Fact]
    public void Ambiguous_keeps_close_candidates_instead_of_rounding_to_known()
    {
        var ambiguous = FakeObservations.Ambiguous("observation-1", "state-a", "state-b");

        // §6.9: 複数候補の差が小さい場合は Known へ丸めない。
        Assert.Equal(StateIdentityStatus.Ambiguous, ambiguous.StateIdentity);
        Assert.Equal(2, ambiguous.StateCandidates.Count);
        var gap = Math.Abs(ambiguous.StateCandidates[0].Confidence - ambiguous.StateCandidates[1].Confidence);
        Assert.True(gap < 0.1, $"Ambiguous の候補差は小さくあるべきです（実際: {gap}）。");
    }

    [Fact]
    public void Invalid_fakes_cannot_be_built()
    {
        Assert.Throws<ArgumentException>(() => FakeObservations.Known(" ", "state-a"));
        Assert.Throws<ArgumentException>(() => FakeObservations.Known("observation-1", " "));
        Assert.Throws<ArgumentException>(() => FakeObservations.Ambiguous("observation-1", "state-a", "state-a"));
        Assert.Throws<ArgumentException>(() => FakeObservations.Unavailable("observation-1", " "));
        Assert.Throws<ArgumentException>(() => FakeObservations.Stale("observation-1", " "));
    }

    [Fact]
    public void Fake_source_serves_the_scripted_statuses_in_order()
    {
        var source = new FakeObservationSource(
        [
            FakeObservations.Known("observation-1", "state-main-menu"),
            FakeObservations.Unknown("observation-2"),
            FakeObservations.Unavailable("observation-3", "capture-black"),
        ]);

        Assert.Equal(StateIdentityStatus.Known, source.Observe(AnyFrame()).StateIdentity);
        Assert.Equal(StateIdentityStatus.InsufficientEvidence, source.Observe(AnyFrame()).StateIdentity);
        Assert.Equal(CaptureAvailability.Unavailable, source.Observe(AnyFrame()).CaptureAvailability);
    }

    [Fact]
    public void Perception_contracts_do_not_know_the_attempt()
    {
        // §6.7 契約4: Observation→Attempt の束縛は Playbook の RunEvent だけが持つ。
        // Perception の wire type には Attempt を参照する field が構造として存在しない。
        var perceptionTypes = new[]
        {
            typeof(ObservationResult), typeof(StateCandidate), typeof(EvidenceRegion), typeof(CapturedFrameReference),
            typeof(ObservedScene), typeof(AffordanceCandidate), typeof(AffordanceLocator),
        };

        foreach (var type in perceptionTypes)
        {
            var attemptCarriers = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.Name.Contains("Attempt", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(attemptCarriers);
        }

        var builderParameters = typeof(FakeObservations)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .SelectMany(method => method.GetParameters())
            .Where(parameter => parameter.Name!.Contains("attempt", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(builderParameters);
    }

    [Fact]
    public void Suite_rejects_contract_violations_per_status()
    {
        var known = FakeObservations.Known("observation-1", "state-a");
        Assert.Throws<InvalidOperationException>(() => ContractConformanceSuite.Verify(known with { StateCandidates = [] }));
        Assert.Throws<InvalidOperationException>(() => ContractConformanceSuite.Verify(known with { ObservationId = " " }));
        Assert.Throws<InvalidOperationException>(() => ContractConformanceSuite.Verify(known with { CaptureFailureReason = "reason-on-known" }));

        var ambiguous = FakeObservations.Ambiguous("observation-1", "state-a", "state-b");
        Assert.Throws<InvalidOperationException>(() => ContractConformanceSuite.Verify(
            ambiguous with { StateCandidates = [ambiguous.StateCandidates[0]] }));

        var unavailable = FakeObservations.Unavailable("observation-1", "capture-black");
        Assert.Throws<InvalidOperationException>(() => ContractConformanceSuite.Verify(unavailable with { CaptureFailureReason = null }));
        Assert.Throws<InvalidOperationException>(() => ContractConformanceSuite.Verify(
            unavailable with { StateCandidates = FakeObservations.Known("observation-2", "state-a").StateCandidates }));
    }
}
