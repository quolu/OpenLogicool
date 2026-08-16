using System.Runtime.InteropServices;

// OpenLogicool Watchdog（計画 §6.2・DEV-009・EXP-IN-03 で採用必須確定）。
// host process が stdin の行 protocol で合成 output の down/up を通知する。
// stdin の EOF（= host の hard crash で pipe が閉じた）を検出したら、追跡中の全 output へ
// 即座に key-up／button-up を送って残留を解消する。通常終了は "EXIT" 行で release なしに終了する。
//
// 依存ゼロ（他 project を参照しない）: watchdog は host が壊れている前提で動く最後の砦であり、
// host 側の library 障害に巻き込まれない。
//
// protocol（1行1命令）:
//   DOWN KEY <hexVK> [EXT]   / UP KEY <hexVK> [EXT]
//   DOWN MOUSE <Left|Right|Middle|X1|X2> / UP MOUSE <name>
//   EXIT
// 不正な行は protocol 破損（host 異常）とみなし、全 release して異常終了する。

var held = new List<HeldOutput>();

while (Console.ReadLine() is { } line)
{
    if (line.Length == 0)
    {
        continue;
    }

    if (line == "EXIT")
    {
        return 0;
    }

    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (!TryParse(parts, out var isDown, out var output))
    {
        Console.Error.WriteLine($"protocol violation: '{line}'. releasing all and exiting.");
        ReleaseAll(held);
        return 1;
    }

    if (isDown)
    {
        held.Add(output);
    }
    else
    {
        held.RemoveAll(candidate => candidate.Equals(output));
    }
}

// stdin EOF = host 死亡（hard crash 含む）。追跡中の全 output を release する。
ReleaseAll(held);
return 0;

static bool TryParse(string[] parts, out bool isDown, out HeldOutput output)
{
    isDown = false;
    output = default;

    if (parts.Length < 3 || parts[0] is not ("DOWN" or "UP"))
    {
        return false;
    }

    isDown = parts[0] == "DOWN";

    switch (parts[1])
    {
        case "KEY" when ushort.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out var vk) && vk is > 0 and <= 0xFE:
            var extended = parts.Length == 4 && parts[3] == "EXT";
            if (parts.Length > 4 || (parts.Length == 4 && !extended))
            {
                return false;
            }

            output = new HeldOutput(IsKey: true, vk, extended, MouseButtonName: "");
            return true;

        case "MOUSE" when parts.Length == 3 && parts[2] is "Left" or "Right" or "Middle" or "X1" or "X2":
            output = new HeldOutput(IsKey: false, 0, false, parts[2]);
            return true;

        default:
            return false;
    }
}

static void ReleaseAll(List<HeldOutput> held)
{
    foreach (var output in held)
    {
        var input = output.IsKey
            ? new INPUT
            {
                type = 1, // INPUT_KEYBOARD
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = output.VirtualKey,
                        dwFlags = 0x0002u | (output.IsExtendedKey ? 0x0001u : 0u), // KEYUP | EXTENDEDKEY
                    },
                },
            }
            : new INPUT
            {
                type = 0, // INPUT_MOUSE
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dwFlags = output.MouseButtonName switch
                        {
                            "Left" => 0x0004u,
                            "Right" => 0x0010u,
                            "Middle" => 0x0040u,
                            _ => 0x0100u, // XUP
                        },
                        mouseData = output.MouseButtonName switch
                        {
                            "X1" => 1u,
                            "X2" => 2u,
                            _ => 0u,
                        },
                    },
                },
            };

        var sent = SendInput(1, [input], Marshal.SizeOf<INPUT>());
        if (sent != 1)
        {
            Console.Error.WriteLine(
                $"release failed for {(output.IsKey ? $"KEY {output.VirtualKey:X2}" : $"MOUSE {output.MouseButtonName}")} (error={Marshal.GetLastWin32Error()}).");
        }
    }
}

[DllImport("user32.dll", SetLastError = true)]
static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

internal readonly record struct HeldOutput(bool IsKey, ushort VirtualKey, bool IsExtendedKey, string MouseButtonName);

[StructLayout(LayoutKind.Sequential)]
internal struct INPUT
{
    public uint type;
    public InputUnion U;
}

[StructLayout(LayoutKind.Explicit)]
internal struct InputUnion
{
    [FieldOffset(0)] public MOUSEINPUT mi;
    [FieldOffset(0)] public KEYBDINPUT ki;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MOUSEINPUT
{
    public int dx;
    public int dy;
    public uint mouseData;
    public uint dwFlags;
    public uint time;
    public nint dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public nint dwExtraInfo;
}
