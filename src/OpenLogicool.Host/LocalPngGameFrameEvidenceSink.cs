using System.IO;
using System.Security.Cryptography;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;

namespace OpenLogicool.Host;

/// <summary>各ObserveのWGC frameをPNG証拠へ保存するlocal adapter。</summary>
public sealed class LocalPngGameFrameEvidenceSink(
    string outputDirectory,
    IGameFramePngEncoder encoder) : IProductGameFrameEvidenceSink
{
    public async ValueTask<CapturedFrameArtifact> SaveAsync(
        CapturedFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Directory.CreateDirectory(outputDirectory);
        var png = encoder.Encode(frame);
        var sha256 = Convert.ToHexString(SHA256.HashData(png.Bytes.Span)).ToLowerInvariant();
        var artifactId = $"frame:{frame.SourceId}:{frame.Sequence}:{sha256[..16]}";
        var safeSource = string.Concat(frame.SourceId.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
        var path = Path.GetFullPath(Path.Combine(
            outputDirectory,
            $"game-frame-{safeSource}-{frame.Sequence:D8}-{sha256[..12]}.png"));
        await File.WriteAllBytesAsync(path, png.Bytes.ToArray(), cancellationToken).ConfigureAwait(false);
        return new CapturedFrameArtifact(
            artifactId,
            "image/png",
            sha256,
            frame.Width,
            frame.Height,
            path);
    }
}
