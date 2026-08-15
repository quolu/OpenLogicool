using System.Collections.ObjectModel;
using OpenLogicool.Contracts.Devices.Shared;

namespace OpenLogicool.Domain;

public sealed record PressOwnershipKey(
    string DeviceInstanceId,
    string ControlId,
    long PressGeneration);

public sealed record PressOwnership(
    PressOwnershipKey Key,
    string ProfileRevision,
    string LayerId,
    string MappingRevision,
    IReadOnlyList<string> Outputs);

public sealed record PressOwnershipDownResult(
    PressOwnershipState State,
    PressOwnership Ownership);

public sealed record PressOwnershipRelease(
    PressOwnershipKey Key,
    IReadOnlyList<string> Outputs);

public sealed record PressOwnershipUpResult(
    PressOwnershipState State,
    PressOwnershipRelease Release);

public sealed record PressOwnershipStopResult(
    PressOwnershipState State,
    IReadOnlyList<PressOwnershipRelease> Releases);

public sealed class PressOwnershipState
{
    private readonly IReadOnlyDictionary<PressOwnershipKey, PressOwnership> _ownerships;

    private PressOwnershipState(
        long generation,
        string profileRevision,
        string layerId,
        string mappingRevision,
        bool acceptsNewDowns,
        IReadOnlyDictionary<PressOwnershipKey, PressOwnership> ownerships)
    {
        Generation = generation;
        ProfileRevision = profileRevision;
        LayerId = layerId;
        MappingRevision = mappingRevision;
        AcceptsNewDowns = acceptsNewDowns;
        _ownerships = ownerships;
    }

    public long Generation { get; }

    public string ProfileRevision { get; }

    public string LayerId { get; }

    public string MappingRevision { get; }

    public bool AcceptsNewDowns { get; }

    public static PressOwnershipState Create(string profileRevision, string layerId, string mappingRevision) =>
        new(
            generation: 0,
            profileRevision,
            layerId,
            mappingRevision,
            acceptsNewDowns: true,
            new ReadOnlyDictionary<PressOwnershipKey, PressOwnership>(new Dictionary<PressOwnershipKey, PressOwnership>()));

    public PressOwnershipState ChangeProfile(string profileRevision, string layerId, string mappingRevision) =>
        new(
            checked(Generation + 1),
            profileRevision,
            layerId,
            mappingRevision,
            AcceptsNewDowns,
            _ownerships);

    public PressOwnershipDownResult Down(PhysicalInput input, IReadOnlyList<string> outputs)
    {
        if (input.Edge != PhysicalInputEdge.Down)
        {
            throw new ArgumentException("PressOwnership の作成には Down 入力が必要です。", nameof(input));
        }

        if (!AcceptsNewDowns)
        {
            throw new InvalidOperationException("新規 Down は停止中です。");
        }

        if (_ownerships.Keys.Any(key => key.DeviceInstanceId == input.DeviceInstanceId && key.ControlId == input.ControlId))
        {
            throw new InvalidOperationException("同じ physical control の PressOwnership は既に存在します。");
        }

        var key = new PressOwnershipKey(input.DeviceInstanceId, input.ControlId, Generation);
        var ownership = new PressOwnership(
            key,
            ProfileRevision,
            LayerId,
            MappingRevision,
            CopyOutputs(outputs));
        var ownerships = CopyOwnerships();
        ownerships.Add(key, ownership);

        return new PressOwnershipDownResult(CreateNext(ownerships, AcceptsNewDowns), ownership);
    }

    public PressOwnershipUpResult Up(PhysicalInput input)
    {
        if (input.Edge != PhysicalInputEdge.Up)
        {
            throw new ArgumentException("PressOwnership の解放には Up 入力が必要です。", nameof(input));
        }

        var ownership = _ownerships.Values.SingleOrDefault(candidate =>
            candidate.Key.DeviceInstanceId == input.DeviceInstanceId &&
            candidate.Key.ControlId == input.ControlId);

        if (ownership is null)
        {
            throw new InvalidOperationException("対応する Down の PressOwnership が存在しません。");
        }

        var ownerships = CopyOwnerships();
        ownerships.Remove(ownership.Key);

        return new PressOwnershipUpResult(
            CreateNext(ownerships, AcceptsNewDowns),
            new PressOwnershipRelease(ownership.Key, ownership.Outputs));
    }

    public PressOwnershipStopResult StopAndReleaseAll()
    {
        var releases = _ownerships.Values
            .OrderBy(ownership => ownership.Key.DeviceInstanceId, StringComparer.Ordinal)
            .ThenBy(ownership => ownership.Key.ControlId, StringComparer.Ordinal)
            .ThenBy(ownership => ownership.Key.PressGeneration)
            .Select(ownership => new PressOwnershipRelease(ownership.Key, ownership.Outputs))
            .ToArray();
        var stoppedState = CreateNext(new Dictionary<PressOwnershipKey, PressOwnership>(), acceptsNewDowns: false);

        return new PressOwnershipStopResult(stoppedState, Array.AsReadOnly(releases));
    }

    private PressOwnershipState CreateNext(
        IReadOnlyDictionary<PressOwnershipKey, PressOwnership> ownerships,
        bool acceptsNewDowns) =>
        new(
            Generation,
            ProfileRevision,
            LayerId,
            MappingRevision,
            acceptsNewDowns,
            new ReadOnlyDictionary<PressOwnershipKey, PressOwnership>(new Dictionary<PressOwnershipKey, PressOwnership>(ownerships)));

    private Dictionary<PressOwnershipKey, PressOwnership> CopyOwnerships() =>
        new(_ownerships);

    private static IReadOnlyList<string> CopyOutputs(IReadOnlyList<string> outputs) =>
        Array.AsReadOnly(outputs.ToArray());
}
