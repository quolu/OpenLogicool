using System.Text;
using System.Text.Json;
using OpenLogicool.Contracts.Devices.Shared;
using OpenLogicool.Devices.G13;

namespace OpenLogicool.Probe;

internal static class G13LcdSmoke
{
    public static int Run(string[] args, string outputDirectory)
    {
        var inspectOnly = args.Contains("--inspect-only", StringComparer.Ordinal);
        var setOutputReportExperiment = args.Contains("--set-output-report", StringComparer.Ordinal);
        var solidPattern = args.Contains("--solid", StringComparer.Ordinal);
        var transport = setOutputReportExperiment ? "HidD_SetOutputReport" : "WriteFile";
        var patternName = solidPattern ? "solid-all-bits-on" : "border-x-center-cross";
        Directory.CreateDirectory(outputDirectory);

        var access = new G13LcdHidAccess();
        var collections = access.EnumerateCollections();
        Console.WriteLine($"[g13-lcd] G13 HID collections: {collections.Count}");
        foreach (var collection in collections)
        {
            Console.WriteLine(
                $"[g13-lcd] usage=0x{collection.UsagePage:X4}:0x{collection.Usage:X4} " +
                $"input={collection.InputReportByteLength} output={collection.OutputReportByteLength} " +
                $"feature={collection.FeatureReportByteLength}");
            Console.WriteLine($"[g13-lcd]   {collection.DevicePath}");
        }

        if (inspectOnly)
        {
            return WriteEvidence(
                outputDirectory, collections, transport, patternName, null, false, [], 0, null, inspectOnly: true);
        }

        using var input = new G13RawInputSource();
        if (input.EnumerateDevices().Count == 0)
        {
            return WriteEvidence(
                outputDirectory, collections, transport, patternName, null, false, [], input.DroppedInputCount,
                "G13 Raw Inputが列挙されませんでした。");
        }

        int? bytesWritten = null;
        var observed = new List<object>();
        try
        {
            var framebuffer = solidPattern
                ? Enumerable.Repeat((byte)0xFF, G13LcdFrame.FramebufferLength).ToArray()
                : G13LcdFrame.CreateIdentificationPattern();
            var report = G13LcdFrame.BuildWireReport(framebuffer);
            using var handle = access.Open();
            bytesWritten = setOutputReportExperiment
                ? handle.SetOutputReportForExperiment(report)
                : handle.Write(report);

            Console.WriteLine($"[g13-lcd] {transport}で{bytesWritten} bytesを送りました。");
            Console.WriteLine(solidPattern
                ? "[g13-lcd] LCD全体が単色へ変わった場合だけ、G1を1回押してください（60秒）。"
                : "[g13-lcd] LCDに外枠・X・中央の縦横線が見えた場合だけ、G1を1回押してください（60秒）。");

            var sawDown = false;
            var sawUp = false;
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline && !sawUp)
            {
                var pulled = false;
                while (input.TryPull(out var edge))
                {
                    pulled = true;
                    observed.Add(new
                    {
                        edge.MonotonicMs,
                        edge.ControlId,
                        Edge = edge.Edge.ToString(),
                        edge.ReportSequence,
                    });

                    if (edge.ControlId == "G1" && edge.Edge == PhysicalInputEdge.Down)
                    {
                        sawDown = true;
                    }
                    else if (edge.ControlId == "G1" && edge.Edge == PhysicalInputEdge.Up && sawDown)
                    {
                        sawUp = true;
                    }
                }

                if (!pulled)
                {
                    Thread.Sleep(5);
                }
            }

            var success = sawDown && sawUp && input.DroppedInputCount == 0;
            return WriteEvidence(
                outputDirectory,
                collections,
                transport,
                patternName,
                bytesWritten,
                success,
                observed,
                input.DroppedInputCount,
                success ? null : "識別patternを確認したG1 down/upを60秒以内に観測できませんでした。");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[g13-lcd] failed: {ex.Message}");
            return WriteEvidence(
                outputDirectory, collections, transport, patternName, bytesWritten, false, observed,
                input.DroppedInputCount, ex.ToString());
        }
    }

    private static int WriteEvidence(
        string outputDirectory,
        IReadOnlyList<G13HidCollectionInfo> collections,
        string transport,
        string patternName,
        int? bytesWritten,
        bool ownerPatternConfirmedByG1,
        IReadOnlyList<object> observedInputs,
        long droppedInputCount,
        string? failure,
        bool inspectOnly = false)
    {
        var outputPath = Path.Combine(outputDirectory, $"g13-lcd-smoke-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        var evidence = new
        {
            Probe = "g13-lcd-smoke",
            CapturedAtUtc = DateTime.UtcNow.ToString("O"),
            Machine = Environment.MachineName,
            OsVersion = Environment.OSVersion.VersionString,
            Collections = collections,
            Transport = transport,
            Pattern = patternName,
            BytesWritten = bytesWritten,
            OwnerPatternConfirmedByG1 = ownerPatternConfirmedByG1,
            ObservedInputs = observedInputs,
            DroppedInputCount = droppedInputCount,
            Failure = failure,
        };
        File.WriteAllText(
            outputPath,
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));

        Console.WriteLine($"[g13-lcd] evidence → {outputPath}");
        if (failure is not null)
        {
            Console.Error.WriteLine($"[g13-lcd] {failure}");
            return 2;
        }

        if (inspectOnly)
        {
            Console.WriteLine("[g13-lcd] inspect-only: caps列挙だけを完了しました。LCD writeと目視確認は未実施です。");
            return 0;
        }

        Console.WriteLine("[g13-lcd] pattern確認とG1 down/up、drop 0が成立しました。");
        return 0;
    }
}
