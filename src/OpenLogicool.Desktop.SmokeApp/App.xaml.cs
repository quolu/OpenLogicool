using System;
using System.Text.Json;
using System.Windows;

namespace OpenLogicool.Desktop.SmokeApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var report = EnvironmentReport.Capture();

        if (Array.IndexOf(e.Args, "--selftest") >= 0)
        {
            Console.WriteLine(JsonSerializer.Serialize(report));
            Shutdown(0);
            return;
        }

        var window = new MainWindow(report);
        window.Show();
    }
}
