using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Shared;
using OpenLogicool.Input;

namespace OpenLogicool.Host;

/// <summary>
/// low-level hookが渡すWindows messageを、記録器が読む生入力edgeへ翻訳する純関数。
/// hook procedureからOSの構造体を読む部分と分けてあるので、翻訳規則そのものは
/// 実機のforegroundを必要とせずに検証できる。
/// </summary>
public static class WindowsDemonstrationInputEdgeFactory
{
    public const int WmKeyDown = 0x0100;
    public const int WmKeyUp = 0x0101;
    public const int WmSysKeyDown = 0x0104;
    public const int WmSysKeyUp = 0x0105;

    public const int WmLButtonDown = 0x0201;
    public const int WmLButtonUp = 0x0202;
    public const int WmRButtonDown = 0x0204;
    public const int WmRButtonUp = 0x0205;
    public const int WmMButtonDown = 0x0207;
    public const int WmMButtonUp = 0x0208;
    public const int WmMouseWheel = 0x020A;
    public const int WmMouseHWheel = 0x020E;
    public const int WmXButtonDown = 0x020B;
    public const int WmXButtonUp = 0x020C;

    /// <summary>KBDLLHOOKSTRUCT.flags の LLKHF_EXTENDED。</summary>
    public const uint ExtendedKeyFlag = 0x01;

    private const int WheelDelta = 120;

    /// <summary>key以外のmessageはnullを返す（記録しない）。</summary>
    public static DemonstrationInputEdge? FromKeyboardMessage(
        int message,
        ushort virtualKey,
        uint flags,
        uint eventTimeMs,
        DateTimeOffset occurredUtc)
    {
        var kind = message switch
        {
            WmKeyDown or WmSysKeyDown => DemonstrationInputEdgeKind.KeyDown,
            WmKeyUp or WmSysKeyUp => DemonstrationInputEdgeKind.KeyUp,
            _ => (DemonstrationInputEdgeKind?)null,
        };

        if (kind is null)
        {
            return null;
        }

        var token = KeyToken(virtualKey, (flags & ExtendedKeyFlag) != 0);
        return new DemonstrationInputEdge(
            ContractSchemaVersions.Revision03,
            DemonstrationInputSource.Keyboard,
            kind.Value,
            token,
            token,
            eventTimeMs,
            occurredUtc);
        // key edgeにScreenPointを載せない——打鍵にpointer位置は属さない。
    }

    /// <summary>button・wheel以外のmessage（移動など）はnullを返す（記録しない）。</summary>
    public static DemonstrationInputEdge? FromMouseMessage(
        int message,
        uint mouseData,
        int screenX,
        int screenY,
        uint eventTimeMs,
        DateTimeOffset occurredUtc)
    {
        var point = new DemonstrationScreenPoint(screenX, screenY);
        var highWord = (short)((mouseData >> 16) & 0xFFFF);

        return message switch
        {
            WmLButtonDown => Button(DemonstrationInputEdgeKind.PointerDown, "Mouse:Left", point, eventTimeMs, occurredUtc),
            WmLButtonUp => Button(DemonstrationInputEdgeKind.PointerUp, "Mouse:Left", point, eventTimeMs, occurredUtc),
            WmRButtonDown => Button(DemonstrationInputEdgeKind.PointerDown, "Mouse:Right", point, eventTimeMs, occurredUtc),
            WmRButtonUp => Button(DemonstrationInputEdgeKind.PointerUp, "Mouse:Right", point, eventTimeMs, occurredUtc),
            WmMButtonDown => Button(DemonstrationInputEdgeKind.PointerDown, "Mouse:Middle", point, eventTimeMs, occurredUtc),
            WmMButtonUp => Button(DemonstrationInputEdgeKind.PointerUp, "Mouse:Middle", point, eventTimeMs, occurredUtc),
            WmXButtonDown => Button(
                DemonstrationInputEdgeKind.PointerDown, XButtonToken(highWord), point, eventTimeMs, occurredUtc),
            WmXButtonUp => Button(
                DemonstrationInputEdgeKind.PointerUp, XButtonToken(highWord), point, eventTimeMs, occurredUtc),
            WmMouseWheel => Wheel(point, eventTimeMs, occurredUtc, highWord / WheelDelta, 0),
            WmMouseHWheel => Wheel(point, eventTimeMs, occurredUtc, 0, highWord / WheelDelta),
            _ => null,
        };
    }

    /// <summary>
    /// virtual keyを既存のoutput token文法へ写す。表に無いkeyは "Vk:0xNN" として残し、
    /// 別の名前へ丸めない。
    /// </summary>
    public static string KeyToken(ushort virtualKey, bool isExtendedKey) =>
        OutputTokens.TryGetKeyName(virtualKey, isExtendedKey, out var keyName)
            ? $"Key:{keyName}"
            : $"Vk:0x{virtualKey:X2}";

    private static string XButtonToken(short highWord) => highWord == 1 ? "Mouse:X1" : "Mouse:X2";

    private static DemonstrationInputEdge Button(
        DemonstrationInputEdgeKind kind,
        string token,
        DemonstrationScreenPoint point,
        uint eventTimeMs,
        DateTimeOffset occurredUtc) =>
        new(
            ContractSchemaVersions.Revision03,
            DemonstrationInputSource.Mouse,
            kind,
            token,
            token,
            eventTimeMs,
            occurredUtc,
            point);

    private static DemonstrationInputEdge? Wheel(
        DemonstrationScreenPoint point,
        uint eventTimeMs,
        DateTimeOffset occurredUtc,
        int verticalSteps,
        int horizontalSteps) =>
        verticalSteps == 0 && horizontalSteps == 0
            ? null
            : new DemonstrationInputEdge(
                ContractSchemaVersions.Revision03,
                DemonstrationInputSource.Mouse,
                DemonstrationInputEdgeKind.Wheel,
                "Mouse:Wheel",
                "Mouse:Wheel",
                eventTimeMs,
                occurredUtc,
                point,
                verticalSteps,
                horizontalSteps);
}
