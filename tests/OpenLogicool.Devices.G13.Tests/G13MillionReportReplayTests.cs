using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Devices.G13;
using Xunit;

namespace OpenLogicool.Devices.G13.Tests;

/// <summary>
/// Phase 2 Exit 条件2: 1,000,000 report replay（G13）。
/// 生成器（固定 LCG）が各 report で加えた変化を oracle として保持し、
/// stream の出力（button edge・stick sample）が加えた変化と完全一致することを全 report で検証する。
/// 台帳の未確認 bit（byte5 bit6-7・byte7 bit1-2/4-6・jitter bit7）へ noise を混ぜ、
/// 既知 control 以外が edge を生まないことも同時に検証する。
/// </summary>
public sealed class G13MillionReportReplayTests
{
    private const int ReportCount = 1_000_000;

    private static readonly (int ByteIndex, int Bit, string ControlId)[] ButtonBits =
    [
        (3, 0, "G1"), (3, 1, "G2"), (3, 2, "G3"), (3, 3, "G4"),
        (3, 4, "G5"), (3, 5, "G6"), (3, 6, "G7"), (3, 7, "G8"),
        (4, 0, "G9"), (4, 1, "G10"), (4, 2, "G11"), (4, 3, "G12"),
        (4, 4, "G13"), (4, 5, "G14"), (4, 6, "G15"), (4, 7, "G16"),
        (5, 0, "G17"), (5, 1, "G18"), (5, 2, "G19"), (5, 3, "G20"),
        (5, 4, "G21"), (5, 5, "G22"),
        (6, 0, "LCD_AUX"), (6, 1, "LCD1"), (6, 2, "LCD2"), (6, 3, "LCD3"), (6, 4, "LCD4"),
        (6, 5, "M1"), (6, 6, "M2"), (6, 7, "M3"),
        (7, 0, "MR"), (7, 3, "STICK_PRESS"),
    ];

    // 台帳で未確認・jitter の bit（edge を生んではならない noise 面）
    private static readonly (int ByteIndex, int Bit)[] UnknownBits =
    [
        (5, 6), (5, 7),
        (7, 1), (7, 2), (7, 4), (7, 5), (7, 6), (7, 7),
    ];

    [Fact]
    public void One_million_generated_reports_replay_with_exact_edges_and_no_stuck_control()
    {
        var lcg = new DeterministicLcg(seed: 0xC21C_2026);
        var stream = new G13ReportStream("replay-device");
        var report = G13ReportParser.IdleReport();
        var heldByControl = new bool[ButtonBits.Length];
        var inputs = new List<PhysicalInput>();

        byte lastX = 0;
        byte lastY = 0;
        var hasStick = false;
        long totalEdges = 0;
        long totalStickSamples = 0;
        long expectedSequence = 0;

        for (var i = 0; i < ReportCount; i++)
        {
            string? flippedControl = null;
            var flippedToDown = false;

            var action = lcg.Next(100);
            if (action < 60)
            {
                // 既知 button bit を1つ反転
                var index = (int)lcg.Next(ButtonBits.Length);
                var (byteIndex, bit, controlId) = ButtonBits[index];
                report[byteIndex] ^= (byte)(1 << bit);
                heldByControl[index] = !heldByControl[index];
                flippedControl = controlId;
                flippedToDown = heldByControl[index];
            }
            else if (action < 85)
            {
                // stick 値の変化
                report[1] = (byte)lcg.Next(256);
                report[2] = (byte)lcg.Next(256);
            }
            else
            {
                // 未確認 bit の noise だけ（edge を生まないこと）
                var (byteIndex, bit) = UnknownBits[(int)lcg.Next(UnknownBits.Length)];
                report[byteIndex] ^= (byte)(1 << bit);
            }

            inputs.Clear();
            stream.Feed(report, monotonicMs: i, inputs, out var stickSample);
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

            // stick sample は (X,Y) が前回 sample から変化した時だけ
            var expectStick = !hasStick || report[1] != lastX || report[2] != lastY;
            if (expectStick)
            {
                Assert.NotNull(stickSample);
                Assert.Equal(report[1], stickSample!.X);
                Assert.Equal(report[2], stickSample.Y);
                lastX = report[1];
                lastY = report[2];
                hasStick = true;
                totalStickSamples++;
            }
            else
            {
                Assert.Null(stickSample);
            }
        }

        // 終端: idle report（stick 中立 0,0 も含む）で保持中の全 control が Up になる
        var expectedFinalUps = ButtonBits.Where((_, index) => heldByControl[index]).Select(b => b.ControlId).ToHashSet();
        inputs.Clear();
        stream.Feed(G13ReportParser.IdleReport(), monotonicMs: ReportCount, inputs, out _);
        Assert.All(inputs, input => Assert.Equal(PhysicalInputEdge.Up, input.Edge));
        Assert.Equal(expectedFinalUps, inputs.Select(input => input.ControlId).ToHashSet());

        // 空回りで成立していないことの下限（LCG 期待値 ~60万 edge・~25万 sample）
        Assert.True(totalEdges > 400_000, $"edge 総数が想定より少ない: {totalEdges}");
        Assert.True(totalStickSamples > 150_000, $"stick sample 総数が想定より少ない: {totalStickSamples}");
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
