using System.Collections.Concurrent;
using OpenLogicool.Contracts.Devices.Shared;

namespace OpenLogicool.Playbooks;

public enum DemonstrationGateState
{
    Free,
    Recording,
    Playing,
}

/// <summary>
/// 記録と再生の排他。low-level hookのinjected flagだけではNano出力と物理入力を
/// 区別できないため、両者が同時に走らないことを構造で保証して自己記録を防ぐ。
/// </summary>
public sealed class DemonstrationRecordingGate
{
    private readonly Lock gate = new();
    private DemonstrationGateState state = DemonstrationGateState.Free;

    public DemonstrationGateState State
    {
        get
        {
            lock (gate)
            {
                return state;
            }
        }
    }

    public bool TryBeginRecording(out string? refusal) =>
        TryEnter(DemonstrationGateState.Recording, "再生中は記録を開始できません。", out refusal);

    public bool TryBeginPlayback(out string? refusal) =>
        TryEnter(DemonstrationGateState.Playing, "記録中は再生を開始できません。", out refusal);

    public void EndRecording() => Leave(DemonstrationGateState.Recording);

    public void EndPlayback() => Leave(DemonstrationGateState.Playing);

    private bool TryEnter(DemonstrationGateState desired, string busyMessage, out string? refusal)
    {
        lock (gate)
        {
            if (state == desired)
            {
                refusal = desired == DemonstrationGateState.Recording
                    ? "既に記録中です。"
                    : "既に再生中です。";
                return false;
            }

            if (state != DemonstrationGateState.Free)
            {
                refusal = busyMessage;
                return false;
            }

            state = desired;
            refusal = null;
            return true;
        }
    }

    private void Leave(DemonstrationGateState held)
    {
        lock (gate)
        {
            if (state != held)
            {
                throw new InvalidOperationException(
                    $"排他を保持していません。現在={state}、解放しようとした状態={held}。");
            }

            state = DemonstrationGateState.Free;
        }
    }
}

/// <summary>
/// fast path workerから記録器へPhysicalInputを渡す非blockingなfan-out。
/// enqueueは待たず、容量を超えた分は捨てて計数する——記録器はDroppedCountが
/// 0でないことを記録の欠落として扱い、黙って続けない。
/// </summary>
public sealed class DemonstrationDeviceEdgeQueue : IPhysicalInputObserver
{
    private readonly ConcurrentQueue<PhysicalInput> queue = new();
    private readonly int capacity;
    private long dropped;
    private long depth;

    public DemonstrationDeviceEdgeQueue(int capacity = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        this.capacity = capacity;
    }

    public long DroppedCount => Interlocked.Read(ref dropped);

    public void OnInput(PhysicalInput input)
    {
        if (Interlocked.Increment(ref depth) > capacity)
        {
            Interlocked.Decrement(ref depth);
            Interlocked.Increment(ref dropped);
            return;
        }

        queue.Enqueue(input);
    }

    public bool TryDequeue(out PhysicalInput input)
    {
        if (queue.TryDequeue(out input!))
        {
            Interlocked.Decrement(ref depth);
            return true;
        }

        input = null!;
        return false;
    }
}
