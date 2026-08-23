using OpenLogicool.Devices.G600;
using Xunit;

namespace OpenLogicool.Devices.G600.Tests;

/// <summary>
/// side remap payload 構築の focused test。
/// レイアウトの正は docs/probes/g600-profile-decode-2026-08-15.md（通常層 31–90・G-Shift 層 94–153・3 bytes/button）。
/// </summary>
public sealed class G600SideRemapTests
{
    // 全 byte が識別可能な合成 F3（offset を値に刻む）。real backup には依存しない。
    private static byte[] SyntheticF3()
    {
        var report = new byte[G600SideRemap.ReportLength];
        for (var i = 0; i < report.Length; i++)
        {
            report[i] = (byte)(i & 0xFF);
        }

        report[0] = 0xF3;
        return report;
    }

    public static TheoryData<int, byte> SideButtonCases()
    {
        var data = new TheoryData<int, byte>();
        for (var button = 9; button <= 20; button++)
        {
            data.Add(button, (byte)(0x68 + (button - 9))); // G9=F13(0x68) … G20=F24(0x73)
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SideButtonCases))]
    public void Side_button_cells_are_rewritten_to_intermediate_usage_in_both_layers(int button, byte usage)
    {
        var modified = G600SideRemap.Build(SyntheticF3());

        foreach (var layerBase in new[] { G600SideRemap.NormalLayerBaseOffset, G600SideRemap.ShiftLayerBaseOffset })
        {
            var offset = layerBase + (button - 1) * G600SideRemap.BytesPerButton;
            Assert.Equal(0x00, modified[offset]);     // mouseCode: keyboard
            Assert.Equal(0x00, modified[offset + 1]); // modifiers: none
            Assert.Equal(usage, modified[offset + 2]);
        }
    }

    [Fact]
    public void All_bytes_outside_side_button_cells_are_preserved()
    {
        var original = SyntheticF3();
        var modified = G600SideRemap.Build(original);

        var rewritten = new HashSet<int>();
        for (var button = 9; button <= 20; button++)
        {
            foreach (var layerBase in new[] { G600SideRemap.NormalLayerBaseOffset, G600SideRemap.ShiftLayerBaseOffset })
            {
                var offset = layerBase + (button - 1) * G600SideRemap.BytesPerButton;
                rewritten.Add(offset);
                rewritten.Add(offset + 1);
                rewritten.Add(offset + 2);
            }
        }

        for (var i = 0; i < original.Length; i++)
        {
            if (!rewritten.Contains(i))
            {
                Assert.Equal(original[i], modified[i]);
            }
        }

        // G1〜G8（通常層・G-Shift 層とも）と G-Shift 層 LED（91–93）が保持される代表確認
        Assert.Equal(original[31], modified[31]);   // 通常層 G1
        Assert.Equal(original[52], modified[52]);   // 通常層 G8 の先頭
        Assert.Equal(original[91], modified[91]);   // G-Shift 層 LED R
        Assert.Equal(original[94], modified[94]);   // G-Shift 層 G1
    }

    [Fact]
    public void Build_does_not_mutate_its_input()
    {
        var original = SyntheticF3();
        var copy = original.ToArray();

        G600SideRemap.Build(original);

        Assert.Equal(copy, original);
    }

    [Fact]
    public void IsApplied_is_false_before_build_and_true_after()
    {
        var original = SyntheticF3();

        Assert.False(G600SideRemap.IsApplied(original));
        Assert.True(G600SideRemap.IsApplied(G600SideRemap.Build(original)));
    }

    [Fact]
    public void Wrong_length_or_report_id_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => G600SideRemap.Build(new byte[153]));

        var wrongId = SyntheticF3();
        wrongId[0] = 0xF4;
        Assert.Throws<ArgumentException>(() => G600SideRemap.Build(wrongId));
    }

    [Fact]
    public void Serial_hid_suppression_disables_g6_through_g20_in_both_layers()
    {
        var modified = G600LegacySuppression.Build(SyntheticF3(), G600LegacySuppressionMode.NoOutput);

        foreach (var layerBase in new[] { G600SideRemap.NormalLayerBaseOffset, G600SideRemap.ShiftLayerBaseOffset })
        {
            for (var button = 6; button <= 20; button++)
            {
                var offset = layerBase + (button - 1) * G600SideRemap.BytesPerButton;
                Assert.Equal([0x00, 0x00, 0x00], modified[offset..(offset + G600SideRemap.BytesPerButton)]);
            }
        }

        Assert.True(G600LegacySuppression.IsApplied(modified, G600LegacySuppressionMode.NoOutput));
        Assert.True(G600LegacySuppression.IsAnyApplied(modified));
    }

    [Fact]
    public void Serial_hid_suppression_preserves_g1_through_g5_and_non_button_bytes()
    {
        var original = SyntheticF3();
        var modified = G600LegacySuppression.Build(original, G600LegacySuppressionMode.NoOutput);

        var rewritten = new HashSet<int>();
        foreach (var layerBase in new[] { G600SideRemap.NormalLayerBaseOffset, G600SideRemap.ShiftLayerBaseOffset })
        {
            for (var button = 6; button <= 20; button++)
            {
                var offset = layerBase + (button - 1) * G600SideRemap.BytesPerButton;
                rewritten.UnionWith([offset, offset + 1, offset + 2]);
            }
        }

        for (var i = 0; i < original.Length; i++)
        {
            if (!rewritten.Contains(i))
            {
                Assert.Equal(original[i], modified[i]);
            }
        }
        Assert.Equal(original, SyntheticF3());
    }

    [Fact]
    public void Suppression_modes_are_distinct_and_unknown_mode_is_rejected()
    {
        var original = SyntheticF3();
        var intermediate = G600LegacySuppression.Build(original, G600LegacySuppressionMode.IntermediateUsage);
        var noOutput = G600LegacySuppression.Build(original, G600LegacySuppressionMode.NoOutput);

        Assert.True(G600LegacySuppression.IsApplied(intermediate, G600LegacySuppressionMode.IntermediateUsage));
        Assert.False(G600LegacySuppression.IsApplied(intermediate, G600LegacySuppressionMode.NoOutput));
        Assert.True(G600LegacySuppression.IsApplied(noOutput, G600LegacySuppressionMode.NoOutput));
        Assert.False(G600LegacySuppression.IsApplied(noOutput, G600LegacySuppressionMode.IntermediateUsage));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            G600LegacySuppression.Build(original, (G600LegacySuppressionMode)99));
    }
}
