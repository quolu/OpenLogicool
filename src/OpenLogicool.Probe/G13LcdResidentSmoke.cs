using System.Text;
using System.Text.Json;
using OpenLogicool.Host;
using OpenLogicool.Input;

namespace OpenLogicool.Probe;

internal static class G13LcdResidentSmoke
{
    public static int Run(string[] args, string outputDirectory)
    {
        var databasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenLogicool",
            "input-studio.db");
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--db" && index + 1 < args.Length)
            {
                databasePath = Path.GetFullPath(args[++index]);
                continue;
            }

            Console.Error.WriteLine($"[g13-lcd-resident] unknown option: {args[index]}");
            return 1;
        }

        Directory.CreateDirectory(outputDirectory);
        using var host = new ResidentInputHost(
            databasePath,
            watchdogExePath: "unused",
            enableTrace: false,
            leftover: null,
            onboardMode: null,
            outputSessionFactory: () => new NoOutputResidentSession());
        var status = host.Start();
        var applied = SpinWait.SpinUntil(
            () => host.G13LcdStatus is { AppliedRevision: > 0 } or { Failure: not null },
            TimeSpan.FromSeconds(5));

        Console.WriteLine($"[g13-lcd-resident] g13={status.G13DeviceInstanceIds.Count} lcd={status.G13LcdStarted}");
        Console.WriteLine($"[g13-lcd-resident] status={JsonSerializer.Serialize(host.G13LcdStatus)}");
        Console.WriteLine("[g13-lcd-resident] 前面アプリに応じたLCD画像を確認してください。終了はこのterminalへEnterを送ります。");
        Console.ReadLine();

        var lcdStatus = host.G13LcdStatus;
        var failure = host.Failure;
        var evidence = new
        {
            Probe = "g13-lcd-resident-smoke",
            CapturedAtUtc = DateTime.UtcNow.ToString("O"),
            Machine = Environment.MachineName,
            OsVersion = Environment.OSVersion.VersionString,
            DatabasePath = databasePath,
            HostStatus = status,
            LcdAppliedWithinFiveSeconds = applied,
            LcdStatus = lcdStatus,
            HostFailure = failure?.ToString(),
            host.DroppedG13InputCount,
            host.DroppedG600InputCount,
            ProfileSwitchDecisions = host.RecentProfileSwitchDecisions(),
        };
        var outputPath = Path.Combine(
            outputDirectory,
            $"g13-lcd-resident-smoke-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        File.WriteAllText(
            outputPath,
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
        Console.WriteLine($"[g13-lcd-resident] evidence → {outputPath}");

        return status.G13LcdStarted &&
               applied &&
               lcdStatus?.Failure is null &&
               failure is null &&
               host.DroppedG13InputCount == 0 &&
               host.DroppedG600InputCount == 0
            ? 0
            : 2;
    }

    private sealed class NoOutputResidentSession : IResidentOutputSession
    {
        private readonly NoOutputEmitter emitter = new();

        public ResidentOutputRoute Route => ResidentOutputRoute.SendInput;

        public IOutputEmitter Emitter => emitter;

        public Exception? BackgroundFailure => null;

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class NoOutputEmitter : IOutputEmitter
    {
        public void Emit(IReadOnlyList<MappedOutputEdge> edges)
        {
        }
    }
}
