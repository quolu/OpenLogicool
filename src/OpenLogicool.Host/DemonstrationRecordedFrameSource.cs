using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenLogicool.Contracts.Capture;

namespace OpenLogicool.Host;

/// <summary>解析対象をSHA-256検証済みのPNG原本だけへ固定する。</summary>
public sealed class DemonstrationRecordedFrameSource : IProductGameFrameSource
{
    private CapturedFrame? selected;

    public async Task SelectAsync(DemonstrationTimelineFrame snapshot, CancellationToken cancellationToken)
    {
        selected = null;
        var bytes = await File.ReadAllBytesAsync(snapshot.Artifact.LocalPath!, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), snapshot.Artifact.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("記録画像のSHA-256が原本と一致しません。");
        using var stream = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource bitmap = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        if (bitmap.PixelWidth != snapshot.Frame.Width || bitmap.PixelHeight != snapshot.Frame.Height)
            throw new InvalidOperationException("記録画像の寸法が原本のframeと一致しません。");
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        selected = snapshot.Frame with { Pixels = new FramePixels(pixels, stride) };
    }

    public ValueTask<CapturedFrame> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(selected ?? throw new InvalidOperationException("解析対象の記録画像が選択されていません。"));
    }
}
