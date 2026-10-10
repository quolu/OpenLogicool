using System.Diagnostics;
using System.Text.Json;
using OpenLogicool.Capture;
using OpenLogicool.Contracts.Audio;
using OpenLogicool.Devices.G13;

namespace OpenLogicool.Probe;

/// <summary>
/// 一つの process の音に合わせて G13 のバックライト色が動くことを、製品と同じ runtime で確かめる実験。
///   --pid N | --process-name NAME   音を追う process
///   --seconds N                     追従する秒数（既定 30）
///   --record FILE                   拾った音を float32 の mono raw で保存する（色の決め方の調整用）
///   --analyze FILE                  保存した音を色の計算へ通し、帯ごとの出方を表示する（機器へは書かない）
/// 音の到着量・周期・色の書き込み数・7 色の出方・明るさの幅を記録し、終了後に色が元へ戻ったかを読む。
/// 送るのは feature report 7（色）だけで、対象の process には触れない。
/// </summary>
internal static class G13BacklightAudioSmoke
{
    public static int Run(string[] args, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var seconds = int.Parse(Option(args, "--seconds") ?? "30");
        var evidence = new Dictionary<string, object?>
        {
            ["Probe"] = "g13-backlight-audio-smoke",
            ["CapturedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["Machine"] = Environment.MachineName,
            ["OsVersion"] = Environment.OSVersion.VersionString,
            ["Arguments"] = args,
        };

        if (Option(args, "--analyze") is { } recorded)
        {
            evidence["Analysis"] = Analyze(recorded);
            return Finish(outputDirectory, evidence, null);
        }

        int processId;
        if (Option(args, "--pid") is { } pidText)
        {
            processId = int.Parse(pidText);
        }
        else if (Option(args, "--process-name") is { } name)
        {
            var candidates = Process.GetProcessesByName(name);
            if (candidates.Length != 1)
            {
                return Finish(outputDirectory, evidence, $"process '{name}' が {candidates.Length} 件あり、一意に選べません。--pid で指定してください。");
            }

            processId = candidates[0].Id;
        }
        else
        {
            Console.Error.WriteLine("[g13-backlight-audio] --pid N か --process-name NAME を指定してください。");
            return 1;
        }

        evidence["TargetProcessId"] = processId;
        evidence["TargetProcessName"] = Process.GetProcessById(processId).ProcessName;

        using (var before = new G13BacklightHidTransport())
        {
            if (!before.TryOpen())
            {
                return Finish(outputDirectory, evidence, "G13 が接続されていません。");
            }

            evidence["ColorBefore"] = before.Read();
        }

        var transport = new RecordingTransport(new G13BacklightHidTransport());
        RecordingSource? source = null;
        var runtime = new G13AudioBacklightRuntime(
            transport,
            pid => source = new RecordingSource(new ProcessLoopbackAudioSource(pid), Option(args, "--record")));
        runtime.SetTarget(processId);
        runtime.Start();
        Console.WriteLine($"[g13-backlight-audio] process {processId} の音を {seconds} 秒間追います。");
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            Thread.Sleep(1000);
            var status = runtime.Status;
            Console.WriteLine(
                $"[g13-backlight-audio] {watch.Elapsed.TotalSeconds,5:0.0}s following={status.IsFollowing} " +
                $"connected={status.IsConnected} writes={status.ColorWrites} failure={status.Failure ?? "なし"}");
        }

        var last = runtime.Status;
        runtime.Stop();
        var elapsed = watch.Elapsed.TotalSeconds;

        using (var after = new G13BacklightHidTransport())
        {
            evidence["ColorAfter"] = after.TryOpen() ? after.Read() : null;
        }

        evidence["Seconds"] = elapsed;
        evidence["Status"] = last;
        evidence["Audio"] = source?.Summary(elapsed);
        evidence["Colors"] = transport.Summary(elapsed);
        var failure = last.Failure
            ?? (source is null ? "音の取り込みが始まりませんでした。" : null)
            ?? (!Equals(evidence["ColorBefore"], evidence["ColorAfter"]) ? "終了後の色が開始前の色と一致しません。" : null);
        return Finish(outputDirectory, evidence, failure);
    }

    /// <summary>保存した音（48 kHz mono float32）を 30 ms ずつ色の計算へ通す。</summary>
    private static object Analyze(string path)
    {
        const int sampleRate = 48000;
        var bytes = File.ReadAllBytes(path);
        var samples = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * sizeof(float));
        var effect = new G13AudioBacklightEffect(sampleRate);
        var tick = sampleRate * 30 / 1000;
        var dominant = new int[G13AudioBacklightEffect.BandCount];
        var share = new double[G13AudioBacklightEffect.BandCount];
        var switches = 0;
        var ticks = 0;
        var brightness = new List<double>();
        var previous = -1;
        for (var offset = 0; offset + tick <= samples.Length; offset += tick, ticks++)
        {
            effect.Process(samples.AsSpan(offset, tick));
            dominant[effect.DominantBand]++;
            if (previous >= 0 && previous != effect.DominantBand)
            {
                switches++;
            }

            previous = effect.DominantBand;
            brightness.Add(effect.Brightness);
            for (var band = 0; band < share.Length; band++)
            {
                share[band] += effect.UsualShare(band);
            }
        }

        brightness.Sort();
        var seconds = (double)samples.Length / sampleRate;
        var result = new
        {
            Seconds = seconds,
            DominantBandShare = dominant.Select(count => Math.Round((double)count / ticks, 3)).ToArray(),
            UsualEnergyShare = share.Select(sum => Math.Round(sum / ticks, 5)).ToArray(),
            DominantChangesPerSecond = Math.Round(switches / seconds, 2),
            BrightnessP10 = Math.Round(brightness[brightness.Count / 10], 3),
            BrightnessMedian = Math.Round(brightness[brightness.Count / 2], 3),
            BrightnessP90 = Math.Round(brightness[brightness.Count * 9 / 10], 3),
        };
        Console.WriteLine($"[g13-backlight-audio] {JsonSerializer.Serialize(result)}");
        return result;
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int Finish(string outputDirectory, Dictionary<string, object?> evidence, string? failure)
    {
        evidence["Failure"] = failure;
        var path = Path.Combine(outputDirectory, $"g13-backlight-audio-smoke-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"[g13-backlight-audio] 記録: {path}");
        if (failure is not null)
        {
            Console.Error.WriteLine($"[g13-backlight-audio] 失敗: {failure}");
            return 1;
        }

        return 0;
    }

    /// <summary>音の到着量と周期を数える。</summary>
    private sealed class RecordingSource(IProcessAudioSource inner, string? recordPath) : IProcessAudioSource
    {
        private readonly BinaryWriter? record = recordPath is null ? null : new BinaryWriter(File.Create(recordPath));
        private long samples;
        private int reads;
        private int emptyReads;
        private double sumOfSquares;
        private float peak;

        public int SampleRate => inner.SampleRate;

        public int Read(Span<float> destination)
        {
            var count = inner.Read(destination);
            reads++;
            if (count == 0)
            {
                emptyReads++;
            }

            samples += count;
            foreach (var sample in destination[..count])
            {
                sumOfSquares += sample * sample;
                peak = Math.Max(peak, Math.Abs(sample));
                record?.Write(sample);
            }

            return count;
        }

        public object Summary(double seconds) => new
        {
            SampleRate,
            Samples = samples,
            SamplesPerSecond = samples / seconds,
            Reads = reads,
            ReadsPerSecond = reads / seconds,
            EmptyReads = emptyReads,
            Rms = samples == 0 ? 0 : Math.Sqrt(sumOfSquares / samples),
            Peak = peak,
        };

        public void Dispose()
        {
            record?.Dispose();
            inner.Dispose();
        }
    }

    /// <summary>書いた色から、7 色の出方と明るさの幅を数える。</summary>
    private sealed class RecordingTransport(IG13BacklightTransport inner) : IG13BacklightTransport
    {
        private readonly List<(G13BacklightColor Color, double Ms)> writes = [];

        public bool TryOpen() => inner.TryOpen();

        public G13BacklightColor Read() => inner.Read();

        public void Write(G13BacklightColor color)
        {
            var started = Stopwatch.GetTimestamp();
            inner.Write(color);
            writes.Add((color, Stopwatch.GetElapsedTime(started).TotalMilliseconds));
        }

        public void Close() => inner.Close();

        public void Dispose() => inner.Dispose();

        public object Summary(double seconds)
        {
            // 最後の1回は追従前の色へ戻す書き込みなので、色の統計からは外す。
            var followed = writes.Count == 0 ? [] : writes[..^1];
            var nearest = new int[G13AudioBacklightEffect.BandCount];
            var switches = 0;
            var previous = -1;
            var brightness = new List<int>();
            foreach (var (color, _) in followed)
            {
                var band = NearestBand(color);
                nearest[band]++;
                if (previous >= 0 && band != previous)
                {
                    switches++;
                }

                previous = band;
                brightness.Add(Math.Max(color.Red, Math.Max(color.Green, color.Blue)));
            }

            brightness.Sort();
            var elapsed = writes.Select(write => write.Ms).Order().ToArray();
            return new
            {
                Writes = writes.Count,
                WritesPerSecond = writes.Count / seconds,
                WriteMsMedian = elapsed.Length == 0 ? 0 : elapsed[elapsed.Length / 2],
                WriteMsMax = elapsed.Length == 0 ? 0 : elapsed[^1],
                NearestRainbowColorCounts = nearest,
                ColorChangesPerSecond = switches / seconds,
                BrightnessMin = brightness.Count == 0 ? 0 : brightness[0],
                BrightnessMedian = brightness.Count == 0 ? 0 : brightness[brightness.Count / 2],
                BrightnessMax = brightness.Count == 0 ? 0 : brightness[^1],
                NotFullySaturated = followed.Count(write => Math.Min(write.Color.Red, Math.Min(write.Color.Green, write.Color.Blue)) != 0),
            };
        }

        private static int NearestBand(G13BacklightColor color)
        {
            double red = color.Red, green = color.Green, blue = color.Blue;
            var max = Math.Max(red, Math.Max(green, blue));
            var range = max - Math.Min(red, Math.Min(green, blue));
            if (range == 0)
            {
                return 0;
            }

            var hue = max == red
                ? 60 * ((green - blue) / range % 6)
                : max == green
                    ? 60 * ((blue - red) / range + 2)
                    : 60 * ((red - green) / range + 4);
            hue = (hue + 360) % 360;
            return Enumerable.Range(0, G13AudioBacklightEffect.BandCount)
                .MinBy(band => Math.Abs((hue - G13AudioBacklightEffect.BandHue(band) + 540) % 360 - 180));
        }
    }
}
