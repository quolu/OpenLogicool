using System.Security.Cryptography;
using OpenLogicool.Contracts.Capture;
using OpenLogicool.Contracts.Perception;

namespace OpenLogicool.Perception;

/// <summary>
/// campaign の fixture と自前 window に限定した、画素 fingerprint による recognizer。
/// 許可されていない source は認識対象外として明示的に拒否する。
/// </summary>
public sealed class FixtureFrameRecognizer : IFrameRecognizer
{
    private readonly string recognizerVersion;
    private readonly Dictionary<FixtureFrameKey, FixtureFrameRule> rules = [];
    private readonly HashSet<string> allowedSourceIds = new(StringComparer.Ordinal);

    public FixtureFrameRecognizer(string recognizerVersion, IEnumerable<FixtureFrameRule> fixtureRules)
    {
        if (string.IsNullOrWhiteSpace(recognizerVersion))
        {
            throw new ArgumentException("RecognizerVersion は空にできません。", nameof(recognizerVersion));
        }

        ArgumentNullException.ThrowIfNull(fixtureRules);
        this.recognizerVersion = recognizerVersion;

        foreach (var rule in fixtureRules)
        {
            ArgumentNullException.ThrowIfNull(rule);
            ValidateRule(rule);

            var key = new FixtureFrameKey(
                rule.SourceId,
                rule.Width,
                rule.Height,
                rule.PixelFormat,
                NormalizeSha256(rule.PixelSha256));
            if (!rules.TryAdd(key, rule))
            {
                throw new ArgumentException("同じ fixture frame の認識規則を重複登録できません。", nameof(fixtureRules));
            }

            allowedSourceIds.Add(rule.SourceId);
        }
    }

    public RecognitionResult Recognize(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (!allowedSourceIds.Contains(frame.SourceId))
        {
            throw new InvalidOperationException("この frame source は fixture recognizer の対象外です。");
        }

        var pixels = frame.Pixels
            ?? throw new InvalidOperationException("fixture recognizer は BGRA8 画素を持つ frame だけを受け取ります。");
        if (pixels.Bgra8.IsEmpty)
        {
            throw new InvalidOperationException("fixture recognizer は空の画素を認識できません。");
        }

        var key = new FixtureFrameKey(
            frame.SourceId,
            frame.Width,
            frame.Height,
            frame.PixelFormat,
            Convert.ToHexString(SHA256.HashData(pixels.Bgra8.Span)));
        if (!rules.TryGetValue(key, out var rule))
        {
            return new RecognitionResult(recognizerVersion, IsCalibrated: true, Candidates: []);
        }

        return new RecognitionResult(
            recognizerVersion,
            rule.IsCalibrated,
            rule.Candidates.ToArray());
    }

    private static void ValidateRule(FixtureFrameRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.SourceId)
            || rule.Width <= 0
            || rule.Height <= 0
            || string.IsNullOrWhiteSpace(rule.PixelFormat)
            || rule.Candidates is null)
        {
            throw new ArgumentException("fixture rule は source、正のサイズ、pixel format、候補集合を持つ必要があります。", nameof(rule));
        }

        _ = NormalizeSha256(rule.PixelSha256);
    }

    private static string NormalizeSha256(string sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256))
        {
            throw new ArgumentException("fixture frame の SHA-256 は空にできません。", nameof(sha256));
        }

        try
        {
            var bytes = Convert.FromHexString(sha256);
            if (bytes.Length != 32)
            {
                throw new ArgumentException("fixture frame の SHA-256 は32 byteである必要があります。", nameof(sha256));
            }

            return Convert.ToHexString(bytes);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("fixture frame の SHA-256 は16進文字列である必要があります。", nameof(sha256), exception);
        }
    }

    private sealed record FixtureFrameKey(
        string SourceId,
        int Width,
        int Height,
        string PixelFormat,
        string PixelSha256);
}

public sealed record FixtureFrameRule(
    string SourceId,
    int Width,
    int Height,
    string PixelFormat,
    string PixelSha256,
    bool IsCalibrated,
    IReadOnlyList<StateCandidate> Candidates);
