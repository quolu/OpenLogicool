using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using OpenLogicool.Contracts.AI;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Domain;

namespace OpenLogicool.Fakes;

public sealed class FakeDeviceInputSource : IDeviceInputSource, IDeviceChangeSource
{
    private readonly IReadOnlyList<DeviceInstance> devices;
    private readonly Queue<PhysicalInput> inputs;
    private readonly Queue<DeviceChange> deviceChanges = new();

    public FakeDeviceInputSource(IEnumerable<DeviceInstance> devices, IEnumerable<PhysicalInput> inputs)
    {
        this.devices = devices.ToArray();
        this.inputs = new Queue<PhysicalInput>(inputs);
    }

    public IReadOnlyList<DeviceInstance> EnumerateDevices() => devices;

    public void EnqueueInput(PhysicalInput input) => inputs.Enqueue(input);

    public void EnqueueChange(DeviceChange change) => deviceChanges.Enqueue(change);

    public bool TryPull(out PhysicalInput input)
    {
        if (inputs.TryDequeue(out var next))
        {
            input = next;
            return true;
        }

        input = null!;
        return false;
    }

    public bool TryPullDeviceChange(out DeviceChange change)
    {
        if (deviceChanges.TryDequeue(out var next))
        {
            change = next;
            return true;
        }

        change = null!;
        return false;
    }
}

public sealed class FakeFrameSource : IFrameSource
{
    private readonly Queue<CapturedFrame> frames;

    public FakeFrameSource(IEnumerable<CapturedFrame> frames) =>
        this.frames = new Queue<CapturedFrame>(frames);

    public FrameReadResult Pull() =>
        frames.TryDequeue(out var frame)
            ? new FrameAvailable(frame)
            : new FrameUnavailable("fake frame script exhausted");
}

public sealed class FakeObservationSource : IObservationSource
{
    private readonly Queue<ObservationResult> observations;

    public FakeObservationSource(IEnumerable<ObservationResult> observations) =>
        this.observations = new Queue<ObservationResult>(observations);

    public static FakeObservationSource FromJsonFile(string filePath) =>
        new(new[] { ContractFixtureDeserializer.DeserializeObservation(File.ReadAllText(filePath)) });

    public ObservationResult Observe(CapturedFrame frame) =>
        observations.TryDequeue(out var observation)
            ? observation
            : throw new InvalidOperationException("fake observation script exhausted");
}

public sealed class FakeNextActionPlanner : INextActionPlanner
{
    private readonly Queue<NextActionProposal> proposals;

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

        this.proposals = new Queue<NextActionProposal>(scriptedProposals);
    }

    public NextActionProposal Propose(PlannerContext plannerContext) =>
        proposals.TryDequeue(out var proposal)
            ? proposal
            : throw new InvalidOperationException("fake proposal script exhausted");
}

public static class ContractFixtureDeserializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(), new ProposalActionJsonConverter() },
    };

    public static ObservationResult DeserializeObservation(string json) =>
        JsonSerializer.Deserialize<ObservationResult>(WithoutFixtureAnnotations(json), JsonOptions)
        ?? throw new JsonException("ObservationResult fixture is null.");

    public static NextActionProposal DeserializeProposal(string json) =>
        JsonSerializer.Deserialize<NextActionProposal>(WithoutFixtureAnnotations(json), JsonOptions)
        ?? throw new JsonException("NextActionProposal fixture is null.");

    private static string WithoutFixtureAnnotations(string json)
    {
        var root = JsonNode.Parse(json) ?? throw new JsonException("fixture JSON is null.");
        RemoveFixtureAnnotations(root);
        return root.ToJsonString();
    }

    private static void RemoveFixtureAnnotations(JsonNode node)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var property in jsonObject.ToArray())
                {
                    if (property.Key.StartsWith("_", StringComparison.Ordinal))
                    {
                        jsonObject.Remove(property.Key);
                    }
                    else if (property.Value is not null)
                    {
                        RemoveFixtureAnnotations(property.Value);
                    }
                }

                break;
            case JsonArray jsonArray:
                foreach (var item in jsonArray)
                {
                    if (item is not null)
                    {
                        RemoveFixtureAnnotations(item);
                    }
                }

                break;
        }
    }

    private sealed class ProposalActionJsonConverter : JsonConverter<ProposalAction>
    {
        public override ProposalAction Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var action = document.RootElement;
            var hasSemanticAction = action.TryGetProperty("semanticActionId", out var semanticActionId);
            var hasVisualTarget = action.TryGetProperty("visualTargetRef", out var visualTargetRef);
            var hasPrimitive = action.TryGetProperty("primitive", out var primitive);
            var schemaVersion = action.TryGetProperty("schemaVersion", out var schemaVersionElement)
                ? schemaVersionElement.GetString() ?? throw new JsonException("action schemaVersion is null.")
                : throw new JsonException("action schemaVersion is required.");

            if (hasSemanticAction && !hasVisualTarget && !hasPrimitive && HasOnlyProperties(action, "schemaVersion", "semanticActionId"))
            {
                return new VerifiedRunAction(
                    schemaVersion,
                    semanticActionId.GetString() ?? throw new JsonException("semanticActionId is null."));
            }

            if (!hasSemanticAction && hasVisualTarget && hasPrimitive && HasOnlyProperties(action, "schemaVersion", "visualTargetRef", "primitive"))
            {
                return new TeachAction(
                    schemaVersion,
                    visualTargetRef.GetString() ?? throw new JsonException("visualTargetRef is null."),
                    primitive.GetString() ?? throw new JsonException("primitive is null."));
            }

            throw new JsonException("action must have exactly one supported proposal shape.");
        }

        public override void Write(Utf8JsonWriter writer, ProposalAction value, JsonSerializerOptions options) =>
            throw new NotSupportedException("fixture deserialization only.");

        private static bool HasOnlyProperties(JsonElement action, params string[] allowedProperties) =>
            action.EnumerateObject().All(property => allowedProperties.Contains(property.Name, StringComparer.Ordinal));
    }
}
