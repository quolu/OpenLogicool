using OpenLogicool.Contracts.Devices.Shared;

namespace OpenLogicool.Input;

public sealed class SerialHidStateFaultException(string message) : Exception(message);

public sealed class SerialHidSnapshot
{
    private readonly byte[] _normalKeys;

    public SerialHidSnapshot(byte modifierMask, IEnumerable<byte> normalKeys, byte mouseButtonMask)
    {
        ArgumentNullException.ThrowIfNull(normalKeys);
        ModifierMask = modifierMask;
        _normalKeys = normalKeys.ToArray();
        if (_normalKeys.Length > SerialHidProtocolV1.MaximumNormalKeys)
        {
            throw new SerialHidStateFaultException("Serial HID v1は通常keyを6個までしか送出できません。");
        }

        NormalKeys = Array.AsReadOnly(_normalKeys);
        MouseButtonMask = mouseButtonMask;
    }

    public byte ModifierMask { get; }
    public IReadOnlyList<byte> NormalKeys { get; }
    public byte MouseButtonMask { get; }

    public byte[] ToSetStatePayload()
    {
        var payload = new byte[SerialHidProtocolV1.SetStatePayloadLength];
        payload[0] = ModifierMask;
        _normalKeys.CopyTo(payload, 1);
        payload[7] = MouseButtonMask;
        return payload;
    }
}

public sealed class PreparedSerialHidState
{
    internal PreparedSerialHidState(
        long baseRevision,
        ushort requestSequence,
        Dictionary<ResolvedOutput, int> ownership,
        SerialHidSnapshot snapshot)
    {
        BaseRevision = baseRevision;
        RequestSequence = requestSequence;
        Ownership = ownership;
        Snapshot = snapshot;
    }

    public long BaseRevision { get; }
    public ushort RequestSequence { get; }
    public SerialHidSnapshot Snapshot { get; }
    internal Dictionary<ResolvedOutput, int> Ownership { get; }
}

/// <summary>
/// Serial HIDのoutput参照数とACK後commitを固定するpure state。
/// Prepareはtentative stateだけを返し、Commitするまで現状態を変更しない。
/// </summary>
public sealed class SerialHidOwnershipState
{
    private Dictionary<ResolvedOutput, int> _ownership = [];

    public long Revision { get; private set; }
    public SerialHidSnapshot CommittedSnapshot => BuildSnapshot(_ownership);

    public PreparedSerialHidState Prepare(IReadOnlyList<MappedOutputEdge> checkpoint, ushort requestSequence)
    {
        if (requestSequence == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestSequence), "request sequenceに0は使えません。");
        }

        if (checkpoint.Count == 0)
        {
            throw new ArgumentException("checkpointは1件以上必要です。", nameof(checkpoint));
        }

        var direction = checkpoint[0].Edge;
        if (checkpoint.Any(edge => edge.Edge != direction))
        {
            throw new ArgumentException("1 checkpointへdownとupを混在させられません。", nameof(checkpoint));
        }

        var tentative = new Dictionary<ResolvedOutput, int>(_ownership);
        foreach (var edge in checkpoint)
        {
            var output = OutputTokens.Parse(edge.Output);
            tentative.TryGetValue(output, out var count);
            if (direction == PhysicalInputEdge.Down)
            {
                tentative[output] = checked(count + 1);
            }
            else if (count == 0)
            {
                throw new SerialHidStateFaultException($"対応するdownがないupです: {edge.Output}");
            }
            else if (count == 1)
            {
                tentative.Remove(output);
            }
            else
            {
                tentative[output] = count - 1;
            }
        }

        return new PreparedSerialHidState(Revision, requestSequence, tentative, BuildSnapshot(tentative));
    }

    public void CommitAck(PreparedSerialHidState prepared, ushort acknowledgedSequence)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (acknowledgedSequence != prepared.RequestSequence)
        {
            throw new SerialHidStateFaultException(
                $"ACK sequence {acknowledgedSequence} は要求 {prepared.RequestSequence} と一致しません。");
        }

        if (prepared.BaseRevision != Revision)
        {
            throw new SerialHidStateFaultException("ACK対象と現在のcommitted revisionが一致しません。");
        }

        _ownership = new Dictionary<ResolvedOutput, int>(prepared.Ownership);
        Revision++;
    }

    private static SerialHidSnapshot BuildSnapshot(IReadOnlyDictionary<ResolvedOutput, int> ownership)
    {
        byte modifiers = 0;
        byte mouseButtons = 0;
        var normalKeys = new SortedSet<byte>();
        foreach (var (output, count) in ownership)
        {
            if (count <= 0)
            {
                throw new InvalidOperationException("output参照数は正でなければなりません。");
            }

            if (output.Kind == ResolvedOutputKind.MouseButton)
            {
                mouseButtons |= output.MouseButton switch
                {
                    MouseButton.Left => 0x01,
                    MouseButton.Right => 0x02,
                    MouseButton.Middle => 0x04,
                    MouseButton.X1 => 0x08,
                    MouseButton.X2 => 0x10,
                    _ => throw new ArgumentOutOfRangeException(nameof(output)),
                };
                continue;
            }

            if (KeyboardHidUsage.TryGetModifierBit(output.VirtualKey, out var bit))
            {
                modifiers |= bit;
            }
            else if (KeyboardHidUsage.TryGetUsage(output.VirtualKey, out var usage))
            {
                normalKeys.Add(usage);
            }
            else
            {
                throw new SerialHidStateFaultException($"VK 0x{output.VirtualKey:X2} はUSB HID usageへ変換できません。");
            }
        }

        if (normalKeys.Count > 6)
        {
            throw new SerialHidStateFaultException("Serial HID v1の6KRO上限を超えました。部分送出しません。");
        }

        return new SerialHidSnapshot(modifiers, normalKeys.ToArray(), mouseButtons);
    }
}

/// <summary>連続する同方向edgeを1個の完全snapshot checkpointへまとめる。</summary>
public static class SerialHidCheckpointBuilder
{
    public static IReadOnlyList<IReadOnlyList<MappedOutputEdge>> Build(IReadOnlyList<MappedOutputEdge> edges)
    {
        var checkpoints = new List<IReadOnlyList<MappedOutputEdge>>();
        var current = new List<MappedOutputEdge>();
        foreach (var edge in edges)
        {
            if (current.Count > 0 && current[0].Edge != edge.Edge)
            {
                checkpoints.Add(current.ToArray());
                current.Clear();
            }

            current.Add(edge);
        }

        if (current.Count > 0)
        {
            checkpoints.Add(current.ToArray());
        }

        return checkpoints;
    }
}
