using System.Runtime.InteropServices;
using System.IO;
using System.Text.Json;
using System.Windows.Automation;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Input;

namespace OpenLogicool.Host;

public sealed record WindowsNanoWindowActivationResult(
    string Strategy,
    int Attempts,
    string? TaskbarButton,
    SerialHidCursorPoint? ScreenPoint,
    string? Receipt);

/// <summary>Windows taskbar／Alt+Tabによる前面化だけを所有するNano OS adapter。</summary>
public static class WindowsTaskbarNanoWindowActivator
{
    private static void Trace(string phase, WindowsGameTarget target, SerialHidCursorPoint? point = null, string? receipt = null)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenLogicool", "diagnostics");
        Directory.CreateDirectory(directory);
        File.AppendAllText(Path.Combine(directory, "nano-window-activation.jsonl"), JsonSerializer.Serialize(new
        {
            Utc = DateTimeOffset.UtcNow, Phase = phase, TargetWindow = (long)target.Window,
            ForegroundWindow = (long)GetForegroundWindow(), TargetMinimized = IsIconic(target.Window),
            Point = point, Receipt = receipt,
        }) + Environment.NewLine);
    }
    public static WindowsNanoWindowActivationResult EnsureForeground(
        WindowsGameTarget target,
        SerialHidProtocolSession session,
        SerialHidEmitter emitter) => ActivateFromTaskbar(target, session, emitter);

    public static WindowsNanoWindowActivationResult ActivateFromTaskbar(
        WindowsGameTarget target,
        SerialHidProtocolSession session,
        SerialHidEmitter emitter)
        => EnsureForeground(() => GetForegroundWindow() == target.Window,
            () => PrepareTaskbarActivation(target, session, emitter));

    internal static WindowsNanoWindowActivationResult EnsureForeground(
        Func<bool> isForeground,
        Func<Func<WindowsNanoWindowActivationResult>> prepareActivation)
    {
        if (isForeground()) return AlreadyForeground();
        var activate = prepareActivation();
        // タスクバー探索中に確認窓が閉じ、ゲームへ戻ることがある。前面のボタンは最小化を起こす。
        return isForeground() ? AlreadyForeground() : activate();
    }

    private static WindowsNanoWindowActivationResult AlreadyForeground() =>
        new("AlreadyForeground", 0, null, null, null);

    private static Func<WindowsNanoWindowActivationResult> PrepareTaskbarActivation(
        WindowsGameTarget target,
        SerialHidProtocolSession session,
        SerialHidEmitter emitter)
    {
        var condition = new PropertyCondition(
            AutomationElement.ClassNameProperty,
            "Taskbar.TaskListButtonAutomationPeer");
        var candidates = AutomationElement.RootElement
            .FindAll(TreeScope.Descendants, condition)
            .Cast<AutomationElement>()
            .Where(element => element.Current.IsEnabled
                && !element.Current.IsOffscreen
                && MatchesExecutable(element.Current.AutomationId, target.ExecutablePath))
            .ToArray();
        if (candidates.Length == 0)
            throw new InvalidOperationException($"ゲーム本体に対応するタスクバーボタンがありません: {target.ExecutablePath}");

        var oracle = new WindowsSerialHidCursorOracle();
        var current = oracle.ReadCurrent();
        var selected = candidates.MinBy(element =>
        {
            var bounds = element.Current.BoundingRectangle;
            var x = bounds.Left + bounds.Width / 2;
            var y = bounds.Top + bounds.Height / 2;
            return Math.Pow(x - current.X, 2) + Math.Pow(y - current.Y, 2);
        })!;
        var selectedBounds = selected.Current.BoundingRectangle;
        var point = new SerialHidCursorPoint(
            checked((int)Math.Round(selectedBounds.Left + selectedBounds.Width / 2)),
            checked((int)Math.Round(selectedBounds.Top + selectedBounds.Height / 2)));
        var buttonName = selected.Current.Name;
        return () =>
        {
            Trace("before-taskbar-click", target, point);
            var receipt = new SerialHidNanoGameInputDevice(session, emitter, oracle).Click(point);
            Thread.Sleep(250);
            Trace("after-taskbar-click", target, point, receipt);
            if (GetForegroundWindow() != target.Window)
                throw new InvalidOperationException("taskbar buttonをNano clickしてもtarget windowがforegroundになりませんでした。");
            return new WindowsNanoWindowActivationResult("TaskbarSemanticButton", 1, buttonName, point, receipt);
        };
    }

    internal static bool MatchesExecutable(string automationId, string executablePath) =>
        string.Equals(automationId, $"Appid: {Path.GetFullPath(executablePath)}", StringComparison.OrdinalIgnoreCase);

    public static WindowsNanoWindowActivationResult ActivateByAltTab(
        WindowsGameTarget target,
        SerialHidEmitter emitter)
    {
        var attempts = 0;
        while (GetForegroundWindow() != target.Window && attempts < 20)
        {
            emitter.Emit(
            [
                new MappedOutputEdge("Key:LAlt", PhysicalInputEdge.Down),
                new MappedOutputEdge("Key:Tab", PhysicalInputEdge.Down),
            ]);
            emitter.Emit(
            [
                new MappedOutputEdge("Key:Tab", PhysicalInputEdge.Up),
                new MappedOutputEdge("Key:LAlt", PhysicalInputEdge.Up),
            ]);
            attempts++;
            Thread.Sleep(250);
        }
        if (GetForegroundWindow() != target.Window)
            throw new InvalidOperationException("Nano Alt+Tabでtarget windowを前面化できませんでした。fallbackせず停止します。");
        return new WindowsNanoWindowActivationResult("AltTab", attempts, null, null, null);
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);
}
