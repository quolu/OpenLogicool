using System.Windows;

namespace OpenLogicool.Desktop;

/// <summary>
/// 診断画面（t09 第4段残作業③）。メイン画面から撤去した device 台帳（<see cref="DeviceLedgerView"/>）を
/// 別ウィンドウとして復活させる。メイン画面には入口を1つだけ置く（目立たない位置）。
/// </summary>
public sealed class DiagnosticsWindow : Window
{
    public DiagnosticsWindow(InputStudioReport report)
    {
        Title = "OpenLogicool Input Studio — 診断";
        Background = Theme.Bg;
        Foreground = Theme.Text;
        Width = 900;
        Height = 700;
        Content = new DeviceLedgerView(report);
    }
}
