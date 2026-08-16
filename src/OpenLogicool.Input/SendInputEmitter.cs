using System.Runtime.InteropServices;
using OpenLogicool.Contracts.Devices.Shared;

namespace OpenLogicool.Input;

public interface IOutputEmitter
{
    /// <summary>output edge 列を送出する。部分成功は fault であり自動再送しない（計画 §6.5）。</summary>
    void Emit(IReadOnlyList<MappedOutputEdge> edges);
}

/// <summary>SendInput の部分成功（送出数不一致）。同じ列を自動再送してはならない。</summary>
public sealed class OutputEmitFaultException(string message) : Exception(message);

/// <summary>
/// Windows SendInput による output edge の実送出。
/// 一括呼び出し（chord の down 集合は単一 SendInput call）で送り、
/// 戻り値が入力数と一致しない場合は OutputEmitFaultException を送出して止まる。
/// UIPI（elevated foreground への不達）は SendInput の戻り値に現れないため、ここでは検出しない（EXP-IN-01）。
/// </summary>
public sealed class SendInputEmitter : IOutputEmitter
{
    public void Emit(IReadOnlyList<MappedOutputEdge> edges)
    {
        if (edges.Count == 0)
        {
            return;
        }

        var inputs = new INPUT[edges.Count];
        for (var i = 0; i < edges.Count; i++)
        {
            inputs[i] = BuildInput(OutputTokens.Parse(edges[i].Output), edges[i].Edge);
        }

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            throw new OutputEmitFaultException(
                $"SendInput が {inputs.Length} 件中 {sent} 件しか受理しませんでした（error={Marshal.GetLastWin32Error()}）。自動再送しません。");
        }
    }

    private static INPUT BuildInput(ResolvedOutput output, PhysicalInputEdge edge)
    {
        if (output.Kind == ResolvedOutputKind.Key)
        {
            var flags = 0u;
            if (edge == PhysicalInputEdge.Up)
            {
                flags |= 0x0002; // KEYEVENTF_KEYUP
            }

            if (output.IsExtendedKey)
            {
                flags |= 0x0001; // KEYEVENTF_EXTENDEDKEY
            }

            return new INPUT
            {
                type = 1, // INPUT_KEYBOARD
                U = new InputUnion { ki = new KEYBDINPUT { wVk = output.VirtualKey, dwFlags = flags } },
            };
        }

        var (downFlag, upFlag, mouseData) = output.MouseButton switch
        {
            MouseButton.Left => (0x0002u, 0x0004u, 0u),
            MouseButton.Right => (0x0008u, 0x0010u, 0u),
            MouseButton.Middle => (0x0020u, 0x0040u, 0u),
            MouseButton.X1 => (0x0080u, 0x0100u, 1u), // XBUTTON1
            MouseButton.X2 => (0x0080u, 0x0100u, 2u), // XBUTTON2
            _ => throw new ArgumentOutOfRangeException(nameof(output)),
        };

        return new INPUT
        {
            type = 0, // INPUT_MOUSE
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = edge == PhysicalInputEdge.Down ? downFlag : upFlag,
                    mouseData = mouseData,
                },
            },
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }
}
