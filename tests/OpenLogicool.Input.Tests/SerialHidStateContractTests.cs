using OpenLogicool.Contracts.Devices.Shared;
using Xunit;

namespace OpenLogicool.Input.Tests;

public sealed class SerialHidStateContractTests
{
    private static MappedOutputEdge Down(string output) => new(output, PhysicalInputEdge.Down);
    private static MappedOutputEdge Up(string output) => new(output, PhysicalInputEdge.Up);

    [Fact]
    public void Chord_and_mouse_are_one_complete_snapshot_and_commit_only_after_ack()
    {
        var state = new SerialHidOwnershipState();
        var checkpoint = new[] { Down("Key:LCtrl"), Down("Key:C"), Down("Mouse:Middle") };

        var prepared = state.Prepare(checkpoint, requestSequence: 1);

        Assert.Equal(0, state.Revision);
        Assert.Equal([], state.CommittedSnapshot.NormalKeys);
        Assert.Equal((byte)0x01, prepared.Snapshot.ModifierMask);
        Assert.Equal([0x06], prepared.Snapshot.NormalKeys);
        Assert.Equal((byte)0x04, prepared.Snapshot.MouseButtonMask);
        Assert.Equal([0x01, 0x06, 0, 0, 0, 0, 0, 0x04], prepared.Snapshot.ToSetStatePayload());

        state.CommitAck(prepared, acknowledgedSequence: 1);
        Assert.Equal(1, state.Revision);
        Assert.Equal([0x06], state.CommittedSnapshot.NormalKeys);
    }

    [Fact]
    public void Finite_sequence_keeps_each_down_and_up_checkpoint()
    {
        var edges = new[]
        {
            Down("Key:LCtrl"), Down("Key:C"), Up("Key:C"), Up("Key:LCtrl"),
            Down("Key:Enter"), Up("Key:Enter"),
        };

        var checkpoints = SerialHidCheckpointBuilder.Build(edges);

        Assert.Equal(4, checkpoints.Count);
        Assert.All(checkpoints[0], edge => Assert.Equal(PhysicalInputEdge.Down, edge.Edge));
        Assert.All(checkpoints[1], edge => Assert.Equal(PhysicalInputEdge.Up, edge.Edge));
        Assert.All(checkpoints[2], edge => Assert.Equal(PhysicalInputEdge.Down, edge.Edge));
        Assert.All(checkpoints[3], edge => Assert.Equal(PhysicalInputEdge.Up, edge.Edge));

        var state = new SerialHidOwnershipState();
        var snapshots = new List<byte[]>();
        ushort sequence = 1;
        foreach (var checkpoint in checkpoints)
        {
            var prepared = state.Prepare(checkpoint, sequence);
            snapshots.Add(prepared.Snapshot.ToSetStatePayload());
            state.CommitAck(prepared, sequence);
            sequence++;
        }

        Assert.Equal((byte)0x01, snapshots[0][0]);
        Assert.Equal((byte)0x06, snapshots[0][1]);
        Assert.All(snapshots[1], value => Assert.Equal((byte)0, value));
        Assert.Equal((byte)0x28, snapshots[2][1]);
        Assert.All(snapshots[3], value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public void Duplicate_ownership_requires_both_ups_before_release()
    {
        var state = new SerialHidOwnershipState();
        Commit(state, [Down("Key:A")]);
        Commit(state, [Down("Key:A")]);

        Commit(state, [Up("Key:A")]);
        Assert.Equal([0x04], state.CommittedSnapshot.NormalKeys);

        Commit(state, [Up("Key:A")]);
        Assert.Equal([], state.CommittedSnapshot.NormalKeys);
    }

    [Fact]
    public void Seventh_key_faults_before_partial_snapshot_or_commit()
    {
        var state = new SerialHidOwnershipState();
        Commit(state, [Down("Key:A"), Down("Key:B"), Down("Key:C"), Down("Key:D"), Down("Key:E"), Down("Key:F")]);
        var revision = state.Revision;
        var committed = state.CommittedSnapshot.NormalKeys;

        Assert.Throws<SerialHidStateFaultException>(() => state.Prepare([Down("Key:G")], requestSequence: 7));

        Assert.Equal(revision, state.Revision);
        Assert.Equal(committed, state.CommittedSnapshot.NormalKeys);
        Assert.Equal(6, state.CommittedSnapshot.NormalKeys.Count);
    }

    [Fact]
    public void Wrong_up_and_stale_ack_are_explicit_faults()
    {
        var state = new SerialHidOwnershipState();
        Assert.Throws<SerialHidStateFaultException>(() => state.Prepare([Up("Key:A")], requestSequence: 1));

        var first = state.Prepare([Down("Key:A")], requestSequence: 1);
        Assert.Throws<SerialHidStateFaultException>(() => state.CommitAck(first, acknowledgedSequence: 2));
        Assert.Equal(0, state.Revision);
        state.CommitAck(first, acknowledgedSequence: 1);
        Assert.Throws<SerialHidStateFaultException>(() => state.CommitAck(first, acknowledgedSequence: 1));
    }

    [Fact]
    public void Prepared_snapshot_cannot_be_mutated_away_from_committed_ownership()
    {
        var state = new SerialHidOwnershipState();
        var prepared = state.Prepare([Down("Key:A")], requestSequence: 1);
        var detached = prepared.Snapshot.NormalKeys.ToArray();
        detached[0] = 0x05; // 呼出側のcopyをBへ変えてもprepared stateはAのまま

        Assert.Throws<NotSupportedException>(
            () => ((IList<byte>)prepared.Snapshot.NormalKeys)[0] = 0x05);
        Assert.Equal([0x04], prepared.Snapshot.NormalKeys);
        Assert.Equal((byte)0x04, prepared.Snapshot.ToSetStatePayload()[1]);

        state.CommitAck(prepared, acknowledgedSequence: 1);
        Assert.Equal([0x04], state.CommittedSnapshot.NormalKeys);
    }

    [Fact]
    public void Unsupported_usage_faults_before_commit()
    {
        var state = new SerialHidOwnershipState();

        Assert.Throws<SerialHidStateFaultException>(() => state.Prepare([Down("Vk:0xE8")], requestSequence: 1));
        Assert.Equal(0, state.Revision);
        Assert.Equal([], state.CommittedSnapshot.NormalKeys);
    }

    private static void Commit(SerialHidOwnershipState state, IReadOnlyList<MappedOutputEdge> checkpoint) =>
        state.CommitAck(
            state.Prepare(checkpoint, (ushort)(state.Revision + 1)),
            (ushort)(state.Revision + 1));
}
