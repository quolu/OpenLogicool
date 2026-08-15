using System.Windows;

namespace OpenLogicool.Desktop.SmokeApp;

public partial class MainWindow : Window
{
    public MainWindow(EnvironmentReport report)
    {
        InitializeComponent();

        FrameworkText.Text = $".NET runtime: {report.FrameworkDescription}";
        OsText.Text = $"OS: {report.OsDescription}";
        ArchText.Text = $"Process architecture: {report.ProcessArchitecture}";
        StartedAtText.Text = $"Started at (UTC): {report.StartedAtUtc:O}";
    }
}
