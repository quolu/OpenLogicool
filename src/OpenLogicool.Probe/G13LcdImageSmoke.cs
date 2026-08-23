using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenLogicool.Devices.G13;

namespace OpenLogicool.Probe;

internal static class G13LcdImageSmoke
{
    public static int Run(string[] args, string outputDirectory)
    {
        if (!TryParse(args, out var input, out var error))
        {
            Console.Error.WriteLine($"[g13-lcd-image] {error}");
            return 1;
        }

        try
        {
            var image = DecodeBgra32(input.ImagePath);
            var conversion = G13LcdBitmapConverter.ConvertBgra32(
                image.Pixels,
                image.Width,
                image.Height,
                image.Stride,
                new G13LcdBitmapConversionOptions(
                    input.Fit,
                    input.Threshold,
                    input.Threshold,
                    AutoCrop: true,
                    input.Invert));

            using var runtime = new G13LcdRuntime(new G13LcdHidTransport());
            var requestedRevision = runtime.RequestFrame(conversion.Framebuffer);
            runtime.Start();
            var applied = SpinWait.SpinUntil(
                () => runtime.Status.AppliedRevision >= requestedRevision || runtime.Status.Failure is not null,
                TimeSpan.FromSeconds(5));
            var liveStatus = runtime.Status;

            Console.WriteLine($"[g13-lcd-image] source={image.Width}x{image.Height}");
            Console.WriteLine(
                $"[g13-lcd-image] crop={conversion.SourceCropX},{conversion.SourceCropY} " +
                $"{conversion.SourceCropWidth}x{conversion.SourceCropHeight}");
            Console.WriteLine(
                $"[g13-lcd-image] destination={conversion.DestinationX},{conversion.DestinationY} " +
                $"{conversion.DestinationWidth}x{conversion.DestinationHeight} fit={input.Fit} threshold={input.Threshold}");
            Console.WriteLine($"[g13-lcd-image] status={JsonSerializer.Serialize(liveStatus)}");
            Console.WriteLine("[g13-lcd-image] LCDの画像を確認してください。終了はこのterminalへEnterを送ります。");
            Console.ReadLine();

            Directory.CreateDirectory(outputDirectory);
            var finalStatus = runtime.Status;
            var evidence = new
            {
                Probe = "g13-lcd-image-smoke",
                CapturedAtUtc = DateTime.UtcNow.ToString("O"),
                Machine = Environment.MachineName,
                OsVersion = Environment.OSVersion.VersionString,
                ImagePath = input.ImagePath,
                SourceWidth = image.Width,
                SourceHeight = image.Height,
                input.Fit,
                input.Threshold,
                input.Invert,
                Crop = new
                {
                    X = conversion.SourceCropX,
                    Y = conversion.SourceCropY,
                    Width = conversion.SourceCropWidth,
                    Height = conversion.SourceCropHeight,
                },
                Destination = new
                {
                    X = conversion.DestinationX,
                    Y = conversion.DestinationY,
                    Width = conversion.DestinationWidth,
                    Height = conversion.DestinationHeight,
                },
                AppliedWithinFiveSeconds = applied,
                Status = finalStatus,
            };
            var outputPath = Path.Combine(
                outputDirectory,
                $"g13-lcd-image-smoke-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
            File.WriteAllText(
                outputPath,
                JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
            Console.WriteLine($"[g13-lcd-image] evidence → {outputPath}");

            return applied &&
                   finalStatus.IsConnected &&
                   finalStatus.AppliedRevision >= requestedRevision &&
                   finalStatus.Failure is null
                ? 0
                : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[g13-lcd-image] {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static DecodedImage DecodeBgra32(string imagePath)
    {
        using var stream = File.OpenRead(imagePath);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
        {
            throw new InvalidDataException("画像にframeがありません。");
        }

        var source = decoder.Frames[0];
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = checked(converted.PixelWidth * 4);
        var pixels = new byte[checked(stride * converted.PixelHeight)];
        converted.CopyPixels(pixels, stride, 0);
        return new DecodedImage(pixels, converted.PixelWidth, converted.PixelHeight, stride);
    }

    private static bool TryParse(string[] args, out Input input, out string error)
    {
        string? imagePath = null;
        var fit = G13LcdBitmapFit.Stretch;
        byte threshold = 180;
        var invert = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--fit" when index + 1 < args.Length:
                    if (!Enum.TryParse<G13LcdBitmapFit>(args[++index], ignoreCase: true, out fit))
                    {
                        input = default!;
                        error = "--fit は stretch または contain です。";
                        return false;
                    }
                    break;
                case "--threshold" when index + 1 < args.Length:
                    if (!byte.TryParse(args[++index], out threshold))
                    {
                        input = default!;
                        error = "--threshold は0〜255です。";
                        return false;
                    }
                    break;
                case "--invert":
                    invert = true;
                    break;
                default:
                    if (args[index].StartsWith("--", StringComparison.Ordinal) || imagePath is not null)
                    {
                        input = default!;
                        error = $"unknown argument: {args[index]}";
                        return false;
                    }
                    imagePath = Path.GetFullPath(args[index]);
                    break;
            }
        }

        if (imagePath is null)
        {
            input = default!;
            error = "usage: g13-lcd-image-smoke <imagePath> [--fit stretch|contain] [--threshold 0..255] [--invert]";
            return false;
        }

        if (!File.Exists(imagePath))
        {
            input = default!;
            error = $"image not found: {imagePath}";
            return false;
        }

        input = new Input(imagePath, fit, threshold, invert);
        error = string.Empty;
        return true;
    }

    private sealed record Input(string ImagePath, G13LcdBitmapFit Fit, byte Threshold, bool Invert);

    private sealed record DecodedImage(byte[] Pixels, int Width, int Height, int Stride);
}
