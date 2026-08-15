using System.IO;
using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Domain;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Domain;
using OpenLogicool.Fakes;
using Xunit;

namespace OpenLogicool.Conformance.Tests;

public sealed class FakeAndFixtureConformanceTests
{
    [Fact]
    public void Fakes_satisfy_the_injected_contract_conformance_suite()
    {
        var input = new PhysicalInput("0.1.0", "device-1", "G1", PhysicalInputEdge.Down, 1.0, 1);
        var device = new DeviceInstance("0.1.0", "device-1", 1133, 2580, "fake-path", "fake-container", 1, Array.Empty<string>());
        var fakeDevice = new FakeDeviceInputSource(new[] { device }, new[] { input });
        Assert.Single(fakeDevice.EnumerateDevices());
        Assert.True(fakeDevice.TryPull(out var pulledInput));
        Assert.Equal(input, pulledInput);

        var frame = Frame();
        var fakeFrame = new FakeFrameSource(new[] { frame });
        var frameResult = Assert.IsType<FrameAvailable>(fakeFrame.Pull());
        ContractConformanceSuite.Verify(frameResult.Frame);
        Assert.IsType<FrameUnavailable>(fakeFrame.Pull());

        var fakeObservation = FakeObservationSource.FromJsonFile(FixturePath("observation-result.sample.json"));
        var observation = fakeObservation.Observe(frame);
        ContractConformanceSuite.Verify(observation);

        var proposal = ContractFixtureDeserializer.DeserializeProposal(File.ReadAllText(FixturePath("next-action-proposal.sample.json")));
        var catalog = new SemanticActionCatalog(new[]
        {
            new SemanticAction("0.1.0", "action.close-popup", "Close popup", RiskClass.Low, "{}"),
        });
        var fakePlanner = new FakeNextActionPlanner(new[] { proposal }, catalog);
        ContractConformanceSuite.Verify(fakePlanner.Propose(proposal.PlannerContextRef));
    }

    [Fact]
    public void Recorded_observation_fixtures_deserialize_and_conform()
    {
        foreach (var fixtureName in new[] { "observation-result.sample.json", "observation-result.gamelab-live.json" })
        {
            var observation = ContractFixtureDeserializer.DeserializeObservation(File.ReadAllText(FixturePath(fixtureName)));
            ContractConformanceSuite.Verify(observation);
        }
    }

    [Fact]
    public void Recorded_proposal_fixture_deserializes_and_conforms()
    {
        var proposal = ContractFixtureDeserializer.DeserializeProposal(File.ReadAllText(FixturePath("next-action-proposal.sample.json")));

        ContractConformanceSuite.Verify(proposal);
    }

    private static CapturedFrame Frame() =>
        new(
            "0.1.0",
            "fake-source",
            CaptureBackend.WindowsGraphicsCapture,
            1,
            1.0,
            new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero),
            1920,
            1080,
            "B8G8R8A8_UNorm",
            96,
            96,
            1,
            0,
            0);

    private static string FixturePath(string fileName) =>
        Path.Combine(RepositoryRoot(), "fixtures", "contracts", fileName);

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenLogicool.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("OpenLogicool.sln を含む repository root を特定できません。");
    }
}
