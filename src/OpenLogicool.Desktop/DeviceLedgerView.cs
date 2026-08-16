using System.Windows;
using System.Windows.Controls;

namespace OpenLogicool.Desktop;

/// <summary>
/// 現行 device 台帳（Phase 2 Exit 条件1・4の表示系）。新シェルでは Diagnostics 相当の
/// 暫定右ペインとして埋め込む（設計 docs/ui-design-phase3.md §3.5 段階1）。
/// 内容の組み立ては旧 InputStudioWindow から移設し、InputStudioReportBuilder は変更しない。
/// 固定色（Brushes.DimGray 等）は使わず SystemColors を DynamicResource で参照する（UX-007）。
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
                Text = $"※ {note}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4),
            };
            noteBlock.SetResourceReference(TextBlock.ForegroundProperty, SystemColors.GrayTextBrushKey);
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
            Margin = new Thickness(0, 0, 0, 4),
        });

        foreach (var line in new[]
                 {
                     $"接続: {device.ConnectionSummary}",
                     $"所有・read/write: {device.OwnershipSummary}",
                     $"profile: {device.ProfileSummary}",
                 })
        {
            panel.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap });
        }

        var constraintsHeader = new TextBlock
        {
            Text = "制約（隠さず表示）",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 8, 0, 2),
        };
        panel.Children.Add(constraintsHeader);
        foreach (var constraint in device.Constraints)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"• {constraint}",
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
            ItemsSource = device.ControlRows,
        };
        grid.Columns.Add(TextColumn("control", nameof(ControlRow.ControlId), 90));
        grid.Columns.Add(TextColumn("根拠", nameof(ControlRow.Evidence), 90));
        grid.Columns.Add(TextColumn("capability", nameof(ControlRow.Capability), 110));
        grid.Columns.Add(TextColumn("役割", nameof(ControlRow.Role), 200));
        grid.Columns.Add(TextColumn("割当（layer: outputs）", nameof(ControlRow.BindingSummary), 420));
        panel.Children.Add(grid);

        return panel;
    }

    private static DataGridTextColumn TextColumn(string header, string propertyPath, double width) =>
        new()
        {
            Header = header,
            Binding = new System.Windows.Data.Binding(propertyPath),
            Width = width,
        };
}
