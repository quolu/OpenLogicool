using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Playbooks;
using Xunit;

namespace OpenLogicool.Playbooks.Tests;

public sealed class DemonstrationDeviceEdgeQueueTests
{
    [Fact]
    public void Edges_come_out_in_the_order_the_fast_path_saw_them()
    {
        var queue = new DemonstrationDeviceEdgeQueue();
        queue.OnInput(Edge("G9", PhysicalInputEdge.Down, 1));
        queue.OnInput(Edge("G9", PhysicalInputEdge.Up, 2));

        Assert.True(queue.TryDequeue(out var first));
        Assert.True(queue.TryDequeue(out var second));
        Assert.False(queue.TryDequeue(out _));
        Assert.Equal(1, first.ReportSequence);
        Assert.Equal(2, second.ReportSequence);
        Assert.Equal(0, queue.DroppedCount);
    }

    [Fact]
    public void Over_capacity_the_queue_counts_what_it_dropped_instead_of_blocking_the_fast_path()
    {
        var queue = new DemonstrationDeviceEdgeQueue(capacity: 2);

        queue.OnInput(Edge("G9", PhysicalInputEdge.Down, 1));
        queue.OnInput(Edge("G9", PhysicalInputEdge.Up, 2));
        queue.OnInput(Edge("G9", PhysicalInputEdge.Down, 3));

        Assert.Equal(1, queue.DroppedCount);
        Assert.True(queue.TryDequeue(out var first));
        Assert.Equal(1, first.ReportSequence);

        // 1件出れば空きが戻り、次のedgeはまた入る。捨てた事実は数え続ける。
        queue.OnInput(Edge("G9", PhysicalInputEdge.Up, 4));
        Assert.True(queue.TryDequeue(out _));
        Assert.True(queue.TryDequeue(out var fourth));
        Assert.Equal(4, fourth.ReportSequence);
        Assert.Equal(1, queue.DroppedCount);
    }

    [Fact]
    public void Concurrent_producers_never_lose_an_edge_silently()
    {
        var queue = new DemonstrationDeviceEdgeQueue(capacity: 4096);
        const int producers = 4;
        const int perProducer = 500;

        Parallel.For(0, producers, producer =>
        {
            for (var index = 0; index < perProducer; index++)
            {
                queue.OnInput(Edge("G9", PhysicalInputEdge.Down, (producer * perProducer) + index));
            }
        });

        var drained = 0;
        while (queue.TryDequeue(out _))
        {
            drained++;
        }

        Assert.Equal(producers * perProducer, drained + queue.DroppedCount);
        Assert.Equal(0, queue.DroppedCount);
    }

    private static PhysicalInput Edge(string controlId, PhysicalInputEdge edge, long sequence) =>
        new(ContractSchemaVersions.Revision01, "dev-a", controlId, edge, MonotonicMs: 0, ReportSequence: sequence);
}
