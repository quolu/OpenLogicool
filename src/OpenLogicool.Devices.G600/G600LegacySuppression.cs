namespace OpenLogicool.Devices.G600;

/// <summary>
/// G600 自身から出る legacy keyboard／mouse 出力の抑止方式。
/// SendInput は中間 usage を入力経路として使うため従来の F13〜F24、Serial HID は vendor Raw Input を
/// 直接使うため G6〜G20 の onboard 出力を無効化する。
/// </summary>
public enum G600LegacySuppressionMode
{
    IntermediateUsage,
    NoOutput,
}

public static class G600LegacySuppression
{
    public const int FirstDisabledButton = 6;
    public const int LastDisabledButton = 20;

    public static byte[] Build(byte[] profileF3, G600LegacySuppressionMode mode) => mode switch
    {
        G600LegacySuppressionMode.IntermediateUsage => G600SideRemap.Build(profileF3),
        G600LegacySuppressionMode.NoOutput => BuildNoOutput(profileF3),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "unknown G600 legacy suppression mode"),
    };

    public static bool IsApplied(byte[] profileF3, G600LegacySuppressionMode mode) => mode switch
    {
        G600LegacySuppressionMode.IntermediateUsage => G600SideRemap.IsApplied(profileF3),
        G600LegacySuppressionMode.NoOutput => IsNoOutputApplied(profileF3),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "unknown G600 legacy suppression mode"),
    };

    public static bool IsAnyApplied(byte[] profileF3) =>
        G600SideRemap.IsApplied(profileF3) || IsNoOutputApplied(profileF3);

    private static byte[] BuildNoOutput(byte[] profileF3)
    {
        G600SideRemap.EnsureProfileReport(profileF3);
        var modified = profileF3.ToArray();
        foreach (var layerBase in new[]
                 {
                     G600SideRemap.NormalLayerBaseOffset,
                     G600SideRemap.ShiftLayerBaseOffset,
                 })
        {
            for (var button = FirstDisabledButton; button <= LastDisabledButton; button++)
            {
                modified.AsSpan(G600SideRemap.CellOffset(layerBase, button), G600SideRemap.BytesPerButton).Clear();
            }
        }

        return modified;
    }

    private static bool IsNoOutputApplied(byte[] profileF3)
    {
        G600SideRemap.EnsureProfileReport(profileF3);
        foreach (var layerBase in new[]
                 {
                     G600SideRemap.NormalLayerBaseOffset,
                     G600SideRemap.ShiftLayerBaseOffset,
                 })
        {
            for (var button = FirstDisabledButton; button <= LastDisabledButton; button++)
            {
                var offset = G600SideRemap.CellOffset(layerBase, button);
                if (profileF3[offset] != 0 || profileF3[offset + 1] != 0 || profileF3[offset + 2] != 0)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
