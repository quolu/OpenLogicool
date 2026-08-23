using System.IO;
using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Devices.G13;

namespace OpenLogicool.Host;

/// <summary>
/// 保存済みのプリセット設定からLCD frameを選ぶpure境界。
/// 設定がないプリセットと共通設定ではWindows frameを表示する。
/// </summary>
public static class G13LcdDisplayFrameSelector
{
    private static readonly byte[] WindowsFrame = Convert.FromBase64String(
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAwMDg4ODg4ODw8PAAAADw+Pj4+Pj8/Pz8/Pz8/PwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB8fHx8fHx8fHx8fAAAADx8fHx8fHx8fHx8fHx8fAAAAAAAAAAAAAAACDv7+gAAAAPA+Hv7+8AAAwPz+BgAAIvfiAAAAAPDw4GAgcPDg4AAAAIDA4HAwMGBg//8AAADA4OBgMDBw4ODAAAAw8PAAAAAA8PDgAAAA8PAgAADg4HAwMHAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD+/////////////gAAAP7//////////////////wAAAAAAAAAAAAAAAAAAAz88Pj8HAAAAAw88PD8PAAAAAAA/PwAAAAA/PwAAAAAdPz8/AAAPHz8wMDAwOD8/AAAADx8/ODAwMDgfHwAAAAAHPjg4HwMBBz84OAcAAAAAMTMzNzceHAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQEBAQEDAwMDAwMAAAADAwMDBwcHBwcPDw8PDx8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");

    public static ReadOnlyMemory<byte> Select(WorkspaceG13LcdSetting? setting)
    {
        if (setting is null)
        {
            return WindowsFrame;
        }

        byte[] frame;
        try
        {
            frame = Convert.FromBase64String(setting.FramebufferBase64);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("G13 LCD設定のframebufferがBase64ではありません。", exception);
        }

        if (frame.Length != G13LcdFrame.FramebufferLength)
        {
            throw new InvalidDataException($"G13 LCD設定のframebufferは{G13LcdFrame.FramebufferLength} bytesである必要があります。");
        }

        return frame;
    }

    static G13LcdDisplayFrameSelector()
    {
        if (WindowsFrame.Length != G13LcdFrame.FramebufferLength)
        {
            throw new InvalidOperationException("内蔵G13 LCD frameの長さが960 bytesではありません。");
        }
    }
}
