using OpenLogicool.Contracts.Devices.G600;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Devices.G600;
using Xunit;

namespace OpenLogicool.Devices.G600.Tests;

/// <summary>
/// Phase 2 Exit 条件2: 1,000,000 report replay（G600）。
/// 生成器（固定 LCG）が各 report で加えた bit 変化を oracle として保持し、
/// stream の出力 edge が「加えた変化そのもの」と完全一致することを全 report で検証する。
/// 未使用 byte（5〜31）へは noise を混ぜ、既知 bit 以外が edge を生まないことも同時に検証する。
/// </summary>
public sealed class G600MillionReportReplayTests
{
    private const int ReportCount = 1_000_000;

    private static readonly (int ByteIndex, int Bit, string ControlId)[] ButtonBits =
    [
        (1, 0, "G1"), (1, 1, "G2"), (1, 2, "G3"), (1, 3, "G4"),
        (1, 4, "G5"), (1, 5, "G6"), (1, 6, "G7"), (1, 7, "G8"),
        (2, 0, "G9"), (2, 1, "G10"), (2, 2, "G11"), (2, 3, "G12"),
        (2, 4, "G13"), (2, 5, "G14"), (2, 6, "G15"), (2, 7, "G16"),
        (3, 0, "G17"), (3, 1, "G18"), (3, 2, "G19"), (3, 3, "G20"),
    ];

    [Fact]
    public void One_million_generated_reports_replay_with_exact_edges_and_no_stuck_control()
    {
        var lcg = new DeterministicLcg(seed: 0xC24A_2026);
        var stream = new G600ReportStream("replay-device");
        var report = G600ReportParser.IdleReport();
        var heldByControl = new bool[ButtonBits.Length];
        var inputs = new List<PhysicalInput>();

        long totalEdges = 0;
        long totalWheelTicks = 0;
        long expectedSequence = 0;

        for (var i = 0; i < ReportCount; i++)
        {
            string? flippedControl = null;
            var flippedToDown = false;
            var expectWheel = 0;

            var action = lcg.Next(100);
            if (action < 70)
            {
                // 既知 button bit を1つ反転
                var index = (int)lcg.Next(ButtonBits.Length);
                var (byteIndex, bit, controlId) = ButtonBits[index];
                report[byteIndex] ^= (byte)(1 << bit);
                heldByControl[index] = !heldByControl[index];
                flippedControl = controlId;
                flippedToDown = heldByControl[index];
                report[4] = 0;
            }
            else if (action < 85)
            {
                // 回転 event: button 変化なし・byte4 = ±1
                expectWheel = lcg.Next(2) == 0 ? 1 : -1;
                report[4] = (byte)(sbyte)expectWheel;
            }
            else
            {
                // 未使用領域の noise だけ（edge も tick も出ないこと）
                report[4] = 0;
                report[5 + (int)lcg.Next(27)] = (byte)lcg.Next(256);
            }

            inputs.Clear();
            stream.Feed(report, monotonicMs: i, inputs, out var wheelTick);
            expectedSequence++;

            if (flippedControl is null)
            {
                Assert.Empty(inputs);
            }
            else
            {
                var input = Assert.Single(inputs);
                Assert.Equal(flippedControl, input.ControlId);
                Assert.Equal(flippedToDown ? PhysicalInputEdge.Down : PhysicalInputEdge.Up, input.Edge);
                Assert.Equal(expectedSequence, input.ReportSequence);
                totalEdges++;
            }

            if (expectWheel == 0)
            {
                Assert.Null(wheelTick);
            }
            else
            {
                Assert.NotNull(wheelTick);
                Assert.Equal(expectWheel, wheelTick!.Delta);
                totalWheelTicks++;
            }
        }

        // 終端: idle report で保持中の全 control が Up になり、stuck が残らない
        var expectedFinalUps = ButtonBits.Where((_, index) => heldByControl[index]).Select(b => b.ControlId).ToHashSet();
        inputs.Clear();
        stream.Feed(G600ReportParser.IdleReport(), monotonicMs: ReportCount, inputs, out _);
        Assert.All(inputs, input => Assert.Equal(PhysicalInputEdge.Up, input.Edge));
        Assert.Equal(expectedFinalUps, inputs.Select(input => input.ControlId).ToHashSet());

        // 空回りで成立していないことの下限（LCG 期待値 ~70万 edge・~15万 tick）
        Assert.True(totalEdges > 500_000, $"edge 総数が想定より少ない: {totalEdges}");
        Assert.True(totalWheelTicks > 100_000, $"wheel tick 総数が想定より少ない: {totalWheelTicks}");
    }
}

/// <summary>再現可能な決定的 LCG（テスト専用）。</summary>
internal sealed class DeterministicLcg(ulong seed)
{
    private ulong _state = seed;

    public uint Next(int exclusiveMax)
    {
        _state = _state * 6364136223846793005UL + 1442695040888963407UL;
        return (uint)((_state >> 33) % (uint)exclusiveMax);
    }
}
