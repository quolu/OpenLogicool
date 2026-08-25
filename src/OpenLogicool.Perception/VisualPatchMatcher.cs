using System.Security.Cryptography;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;
using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Perception;

/// <summary>保存位置の小さな輝度指紋を作り、既知icon／画像領域をAIなしで検証する。</summary>
public static class VisualPatchMatcher
{
    private const int SampleSize = 8;

    public static VisualPatchSignature Capture(
        CapturedFrame frame,
        IReadOnlyList<double> normalizedBounds)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Validate(frame, normalizedBounds);
        var luma = Sample(frame, normalizedBounds);
        return new VisualPatchSignature(
            ContractSchemaVersions.Revision03,
            SampleSize,
            SampleSize,
            Convert.ToBase64String(luma),
            Convert.ToHexString(SHA256.HashData(luma)).ToLowerInvariant());
    }

    public static bool Matches(
        VisualPatchSignature expected,
        CapturedFrame frame,
        IReadOnlyList<double> normalizedBounds,
        double maximumMeanAbsoluteDifference = 24)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (expected.SchemaVersion != ContractSchemaVersions.Revision03
            || expected.SampleWidth != SampleSize
            || expected.SampleHeight != SampleSize
            || maximumMeanAbsoluteDifference is < 0 or > 255)
        {
            throw new ArgumentException("visual patch signatureが不正です。", nameof(expected));
        }
        var baseline = Convert.FromBase64String(expected.LumaBase64);
        var actual = Sample(frame, normalizedBounds);
        if (baseline.Length != actual.Length)
        {
            return false;
        }
        var difference = baseline.Zip(actual, (left, right) => Math.Abs(left - right)).Average();
        return difference <= maximumMeanAbsoluteDifference;
    }

    public static double MeanAbsoluteDifference(
        VisualPatchSignature left,
        VisualPatchSignature right) =>
        VisualPatchSignatureComparer.MeanAbsoluteDifference(left, right);

    private static byte[] Sample(CapturedFrame frame, IReadOnlyList<double> bounds)
    {
        Validate(frame, bounds);
        var pixels = frame.Pixels!;
        var result = new byte[SampleSize * SampleSize];
        for (var y = 0; y < SampleSize; y++)
        {
            for (var x = 0; x < SampleSize; x++)
            {
                var normalizedX = bounds[0] + bounds[2] * (x + 0.5) / SampleSize;
                var normalizedY = bounds[1] + bounds[3] * (y + 0.5) / SampleSize;
                var pixelX = Math.Clamp((int)(normalizedX * frame.Width), 0, frame.Width - 1);
                var pixelY = Math.Clamp((int)(normalizedY * frame.Height), 0, frame.Height - 1);
                var offset = pixelY * pixels.Stride + pixelX * 4;
                var span = pixels.Bgra8.Span;
                result[y * SampleSize + x] = (byte)((span[offset + 2] * 77 + span[offset + 1] * 150 + span[offset] * 29) >> 8);
            }
        }
        return result;
    }

    private static void Validate(CapturedFrame frame, IReadOnlyList<double> bounds)
    {
        if (frame.Pixels is null
            || bounds.Count != 4
            || bounds.Any(value => !double.IsFinite(value) || value is < 0 or > 1)
            || bounds[2] <= 0 || bounds[3] <= 0
            || bounds[0] + bounds[2] > 1 || bounds[1] + bounds[3] > 1)
        {
            throw new ArgumentException("visual patchのframeまたはboundsが不正です。");
        }
    }
}
