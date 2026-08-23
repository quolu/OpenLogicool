using System.Diagnostics;
using System.IO;
using System.Windows;

namespace OpenLogicool.Launcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        try
        {
            Process.Start(GuiHostLaunchCommand.Create(AppContext.BaseDirectory, arguments));
            return 0;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"OpenLogicool を起動できませんでした。\n\n{exception.Message}",
                "OpenLogicool",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 1;
        }
    }
}

public static class GuiHostLaunchCommand
{
    public static ProcessStartInfo Create(string baseDirectory, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentNullException.ThrowIfNull(arguments);

        var hostPath = Path.Combine(baseDirectory, "OpenLogicool.Host.exe");
        var firstHostArgument = 0;

        if (arguments.Count > 0)
        {
            if (arguments.Count < 2 || !string.Equals(arguments[0], "--host", StringComparison.Ordinal))
            {
                throw new ArgumentException("起動引数が正しくありません。", nameof(arguments));
            }

            hostPath = Path.GetFullPath(arguments[1]);
            firstHostArgument = 2;
        }

        if (!File.Exists(hostPath))
        {
            throw new FileNotFoundException("OpenLogicool.Host.exe が見つかりません。", hostPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = hostPath,
            WorkingDirectory = Path.GetDirectoryName(hostPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (firstHostArgument == arguments.Count)
        {
            startInfo.ArgumentList.Add("ui");
            startInfo.ArgumentList.Add("--resident");
        }
        else
        {
            for (var index = firstHostArgument; index < arguments.Count; index++)
            {
                startInfo.ArgumentList.Add(arguments[index]);
            }
        }

        return startInfo;
    }
}
