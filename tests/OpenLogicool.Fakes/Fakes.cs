using System.Text.Json;
using System.Text.Json.Serialization;
using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Domain;

namespace OpenLogicool.Fakes;

public sealed class FakeDeviceInputSource : IDeviceInputSource
{
    private readonly IReadOnlyList<DeviceInstance> _devices;
    private readonly Queue<PhysicalInput> _inputs;

    public FakeDeviceInputSource(IEnumerable<DeviceInstance> devices, IEnumerable<PhysicalInput> inputs)
    {
        _devices = devices.ToArray();
        _inputs = new Queue<PhysicalInput>(inputs);
    }

    public IReadOnlyList<DeviceInstance> EnumerateDevices() => _devices;

    public bool TryPull(out PhysicalInput input)
    {
        if (_inputs.TryDequeue(out var next))
        {
            input = next;
            return true;
        }

        input = null!;
        return false;
    }
}

public sealed class FakeFrameSource : IFrameSource
{
    private readonly Queue<CapturedFrame> _frames;

    public FakeFrameSource(IEnumerable<CapturedFrame> frames)
    {
        _frames = new Queue<CapturedFrame>(frames);
    }

    public FrameReadResult Pull() =>
        _frames.TryDequeue(out var frame)
            ? new FrameAvailable(frame)
            : new FrameUnavailable("fake frame script exhausted");
}

public sealed class FakeObservationSource : IObservationSource
{
    private readonly Queue<ObservationResult> _observations;

    public FakeObservationSource(IEnumerable<ObservationResult> observations)
    {
        _observations = new Queue<ObservationResult>(observations);
    }

    public static FakeObservationSource FromJsonFile(string filePath) =>
        new(new[] { ContractFixtureDeserializer.DeserializeObservation(File.ReadAllText(filePath)) });

    public ObservationResult Observe(CapturedFrame frame) =>
        _observations.TryDequeue(out var observation)
            ? observation
            : throw new InvalidOperationException("fake observation script exhausted");
}

public sealed class FakeNextActionPlanner : INextActionPlanner
{
    private readonly Queue<NextActionProposal> _proposals;

    public FakeNextActionPlanner(IEnumerable<NextActionProposal> proposals, SemanticActionCatalog catalog)
    {
        var scriptedProposals = proposals.ToArray();
        foreach (var proposal in scriptedProposals)
        {
            if (proposal.Action is VerifiedRunAction verifiedRunAction)
            {
                _ = catalog.Get(verifiedRunAction.SemanticActionId);
            }
        }

        _proposals = new Queue<NextActionProposal>(scriptedProposals);
    }

    public NextActionProposal Propose(PlannerContext plannerContext) =>
        _proposals.TryDequeue(out var proposal)
            ? proposal
            : throw new InvalidOperationException("fake proposal script exhausted");
}

public static class ContractFixtureDeserializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(), new ProposalActionJsonConverter() },
    };

    public static ObservationResult DeserializeObservation(string json) =>
        JsonSerializer.Deserialize<ObservationResult>(json, JsonOptions)
        ?? throw new JsonException("ObservationResult fixture が null です。");

    public static NextActionProposal DeserializeProposal(string json) =>
        JsonSerializer.Deserialize<NextActionProposal>(json, JsonOptions)
        ?? throw new JsonException("NextActionProposal fixture が null です。");

    private sealed class ProposalActionJsonConverter : JsonConverter<ProposalAction>
    {
        public override ProposalAction Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var action = document.RootElement;
            var hasSemanticAction = action.TryGetProperty("semanticActionId", out var semanticActionId);
            var hasVisualTarget = action.TryGetProperty("visualTargetRef", out var visualTargetRef);
            var hasPrimitive = action.TryGetProperty("primitive", out var primitive);

            if (hasSemanticAction && !hasVisualTarget && !hasPrimitive)
            {
                return new VerifiedRunAction(string.Empty, semanticActionId.GetString() ?? throw new JsonException("semanticActionId が null です。"));
            }

            if (!hasSemanticAction && hasVisualTarget && hasPrimitive)
            {
                return new TeachAction(
                    string.Empty,
                    visualTargetRef.GetString() ?? throw new JsonException("visualTargetRef が null です。"),
                    primitive.GetString() ?? throw new JsonException("primitive が null です。"));
            }

            throw new JsonException("action は VerifiedRun または Teach のいずれか一方の形でなければなりません。");
        }

        public override void Write(Utf8JsonWriter writer, ProposalAction value, JsonSerializerOptions options) =>
            throw new NotSupportedException("fixture deserialization 専用です。");
    }
}
