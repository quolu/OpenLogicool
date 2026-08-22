using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenLogicool.Desktop;

/// <summary>
/// device 台帳（Phase 2 Exit 条件1・4の表示系）。メイン画面からは撤去済みで、
/// <see cref="DiagnosticsWindow"/> の中身として復活させる（t09 第4段残作業③）。
/// InputStudioReportBuilder が持つ内部語彙（capability・Supported・Experimental 等）は
/// DEV-005 の型として compile 時テストが検証する正の表現のためここでは変更せず、
/// 画面表示だけ <see cref="TranslateForDisplay"/> で日本語へ張り替える（禁止語則）。
/// </summary>
public sealed class DeviceLedgerView : UserControl
{
    public DeviceLedgerView(InputStudioReport report)
    {
        var root = new StackPanel { Margin = new Thickness(16) };

        foreach (var note in report.EnvironmentNotes)
        {
            var noteBlock = new TextBlock
            {
                Text = $"※ {TranslateForDisplay(note)}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Theme.Muted,
                Margin = new Thickness(0, 0, 0, 4),
            };
            root.Children.Add(noteBlock);
        }

        foreach (var device in report.Devices)
        {
            root.Children.Add(BuildDeviceSection(device));
        }

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = root,
        };
    }

    private static UIElement BuildDeviceSection(DeviceSection device)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };

        panel.Children.Add(new TextBlock
        {
            Text = device.DeviceKind,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = Theme.Text,
            Margin = new Thickness(0, 0, 0, 4),
        });

        foreach (var line in new[]
                 {
                     $"接続: {TranslateForDisplay(device.ConnectionSummary)}",
                     $"所有・read/write: {TranslateForDisplay(device.OwnershipSummary)}",
                     $"割当先 profile: {TranslateForDisplay(device.ProfileSummary)}",
                 })
        {
            panel.Children.Add(new TextBlock { Text = line, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap });
        }

        var constraintsHeader = new TextBlock
        {
            Text = "制約（隠さず表示）",
            FontWeight = FontWeights.Bold,
            Foreground = Theme.Text,
            Margin = new Thickness(0, 8, 0, 2),
        };
        panel.Children.Add(constraintsHeader);
        foreach (var constraint in device.Constraints)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"• {TranslateForDisplay(constraint)}",
                Foreground = Theme.Text,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 0, 0, 2),
            });
        }

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            Margin = new Thickness(0, 8, 0, 0),
            Background = Theme.Sunken,
            Foreground = Theme.Text,
            RowBackground = Theme.Panel,
            AlternatingRowBackground = Theme.Raised,
            // DataGrid の列ヘッダとセルは既定スタイルが独自の配色（白地・黒文字）を持ち、
            // DataGrid.Foreground を継がない——暗背景で読めなくなるため明示する（実機目視で確認）。
            ColumnHeaderStyle = DarkColumnHeaderStyle(),
            CellStyle = DarkCellStyle(),
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = Theme.Line,
            BorderBrush = Theme.Line,
            ItemsSource = device.ControlRows.Select(ToDisplayRow).ToArray(),
        };
        grid.Columns.Add(TextColumn("コントロール", nameof(ControlDisplayRow.ControlId), 90));
        grid.Columns.Add(TextColumn("根拠", nameof(ControlDisplayRow.Evidence), 90));
        grid.Columns.Add(TextColumn("対応状況", nameof(ControlDisplayRow.CapabilityLabel), 130));
        grid.Columns.Add(TextColumn("役割", nameof(ControlDisplayRow.Role), 200));
        grid.Columns.Add(TextColumn("割当（配置: 送るキー）", nameof(ControlDisplayRow.BindingSummary), 420));
        panel.Children.Add(grid);

        return panel;
    }

    /// <summary>DataGrid 表示専用の行（英語の Capability 値をここで日本語表示へ変換する）。</summary>
    private sealed record ControlDisplayRow(string ControlId, string Evidence, string CapabilityLabel, string Role, string BindingSummary);

    private static ControlDisplayRow ToDisplayRow(ControlRow row) => new(
        row.ControlId,
        row.Evidence,
        TranslateForDisplay(row.Capability),
        TranslateForDisplay(row.Role),
        TranslateForDisplay(row.BindingSummary));

    private static Style DarkColumnHeaderStyle()
    {
        var style = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Theme.Chrome));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Text));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Theme.Line));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        return style;
    }

    private static Style DarkCellStyle()
    {
        var style = new Style(typeof(DataGridCell));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Text));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 3, 8, 3)));
        return style;
    }

    private static DataGridTextColumn TextColumn(string header, string propertyPath, double width) =>
        new()
        {
            Header = header,
            Binding = new System.Windows.Data.Binding(propertyPath),
            Width = width,
        };

    /// <summary>
    /// 画面表示専用の禁止語置換（DEV-005 の内部語彙はデータ層のまま変更しない）。
    /// InputStudioReportBuilder が実際に出力する英語語彙（capability／Supported／Experimental／
    /// Unverified）だけを対象にする（token/assoc/revision/compile/identity/既定 は出現しない）。
    /// </summary>
    private static string TranslateForDisplay(string text) => text
        .Replace("Supported", "対応確認済み", StringComparison.Ordinal)
        .Replace("Experimental", "実験的対応（強い推定）", StringComparison.Ordinal)
        .Replace("Unverified", "未確認", StringComparison.Ordinal)
        .Replace("capability", "対応状況", StringComparison.Ordinal);
}
