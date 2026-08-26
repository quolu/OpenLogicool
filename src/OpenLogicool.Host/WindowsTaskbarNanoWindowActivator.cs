using System.Runtime.InteropServices;
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
    public static WindowsNanoWindowActivationResult EnsureForeground(
        WindowsGameTarget target,
        SerialHidProtocolSession session,
        SerialHidEmitter emitter) =>
        GetForegroundWindow() == target.Window
            ? new WindowsNanoWindowActivationResult("AlreadyForeground", 0, null, null, null)
            : ActivateFromTaskbar(target, session, emitter);

    public static WindowsNanoWindowActivationResult ActivateFromTaskbar(
        WindowsGameTarget target,
        SerialHidProtocolSession session,
        SerialHidEmitter emitter)
    {
        var condition = new PropertyCondition(
            AutomationElement.ClassNameProperty,
            "Taskbar.TaskListButtonAutomationPeer");
        var expectedPrefixes = new[] { target.ProcessName, target.WindowTitle }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => value + " -")
            .ToArray();
        var candidates = AutomationElement.RootElement
            .FindAll(TreeScope.Descendants, condition)
            .Cast<AutomationElement>()
            .Where(element => element.Current.IsEnabled
                && !element.Current.IsOffscreen
                && expectedPrefixes.Any(prefix =>
                    element.Current.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (candidates.Length == 0)
            throw new InvalidOperationException($"taskbarにtarget '{target.ProcessName}' のbuttonがありません。");

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
        var receipt = new SerialHidNanoGameInputDevice(session, emitter, oracle).Click(point);
        Thread.Sleep(250);
        if (GetForegroundWindow() != target.Window)
            throw new InvalidOperationException("taskbar buttonをNano clickしてもtarget windowがforegroundになりませんでした。");
        return new WindowsNanoWindowActivationResult(
            "TaskbarSemanticButton",
            1,
            selected.Current.Name,
            point,
            receipt);
    }

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
}
