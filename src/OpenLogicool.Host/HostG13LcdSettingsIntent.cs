using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenLogicool.Contracts.Devices.G13;
using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Desktop;
using OpenLogicool.Devices.G13;

namespace OpenLogicool.Host;

public sealed class HostG13LcdSettingsIntent : IG13LcdSettingsIntent
{
    private const long MaximumImageFileBytes = 20 * 1024 * 1024;
    private const long MaximumImagePixels = 20_000_000;

    public WorkspaceG13LcdSetting FromImageFile(string imagePath)
    {
        var fullPath = Path.GetFullPath(imagePath);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException("G13 LCD画像が見つかりません。", fullPath);
        }

        if (file.Length > MaximumImageFileBytes)
        {
            throw new InvalidDataException("G13 LCD画像は20MB以下にしてください。");
        }

        using var stream = file.OpenRead();
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
        {
            throw new InvalidDataException("G13 LCD画像にframeがありません。");
        }

        var source = decoder.Frames[0];
        if ((long)source.PixelWidth * source.PixelHeight > MaximumImagePixels)
        {
            throw new InvalidDataException("G13 LCD画像は2000万pixel以下にしてください。");
        }

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = checked(converted.PixelWidth * 4);
        var pixels = new byte[checked(stride * converted.PixelHeight)];
        converted.CopyPixels(pixels, stride, 0);
        var result = G13LcdBitmapConverter.ConvertBgra32(
            pixels,
            converted.PixelWidth,
            converted.PixelHeight,
            stride,
            new G13LcdBitmapConversionOptions(
                Fit: G13LcdBitmapFit.Stretch,
                PixelThreshold: 180,
                ContentThreshold: 180,
                AutoCrop: true));
        return Setting(
            WorkspaceG13LcdContentKind.Image,
            result.Framebuffer,
            SourceName: file.Name,
            Text: null);
    }

    public WorkspaceG13LcdSetting FromText(string text)
    {
        var normalized = text.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("G13 LCDへ表示するテキストが空です。", nameof(text));
        }

        if (normalized.Length > 120)
        {
            throw new ArgumentException("G13 LCDテキストは120文字以下にしてください。", nameof(text));
        }

        var surface = new Border
        {
            Width = G13LcdContract.Width,
            Height = G13LcdContract.Height,
            Background = Brushes.Black,
            Child = new TextBlock
            {
                Text = normalized,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Yu Gothic UI"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = G13LcdContract.Width - 4,
                MaxHeight = G13LcdContract.Height,
            },
        };
        surface.Measure(new Size(G13LcdContract.Width, G13LcdContract.Height));
        surface.Arrange(new Rect(0, 0, G13LcdContract.Width, G13LcdContract.Height));
        surface.UpdateLayout();

        var rendered = new RenderTargetBitmap(
            G13LcdContract.Width,
            G13LcdContract.Height,
            96,
            96,
            PixelFormats.Pbgra32);
        rendered.Render(surface);
        var converted = new FormatConvertedBitmap(rendered, PixelFormats.Bgra32, null, 0);
        var stride = G13LcdContract.Width * 4;
        var pixels = new byte[stride * G13LcdContract.Height];
        converted.CopyPixels(pixels, stride, 0);
        var result = G13LcdBitmapConverter.ConvertBgra32(
            pixels,
            G13LcdContract.Width,
            G13LcdContract.Height,
            stride,
            new G13LcdBitmapConversionOptions(
                Fit: G13LcdBitmapFit.Stretch,
                PixelThreshold: 128,
                ContentThreshold: 128,
                AutoCrop: false));
        return Setting(
            WorkspaceG13LcdContentKind.Text,
            result.Framebuffer,
            SourceName: null,
            Text: normalized);
    }

    private static WorkspaceG13LcdSetting Setting(
        WorkspaceG13LcdContentKind kind,
        byte[] framebuffer,
        string? SourceName,
        string? Text)
    {
        if (framebuffer.Length != G13LcdContract.FramebufferLength)
        {
            throw new InvalidOperationException("G13 LCD変換結果が960 bytesではありません。");
        }

        return new WorkspaceG13LcdSetting(kind, Convert.ToBase64String(framebuffer), SourceName, Text);
    }
}
