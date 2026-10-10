namespace OpenLogicool.Devices.G13;

public readonly record struct G13BacklightColor(byte Red, byte Green, byte Blue);

/// <summary>
/// 音からバックライトの色を決める pure な状態機。
/// 音を低い方から高い方まで 7 つの帯に分け、虹の 7 色を 1 色ずつ割り当てる。
/// 帯ごとに「普段より目立っているか」を、その帯の普段の大きさと揺れ幅で測って比べ、
/// いちばん目立っている帯の色を出す（低音が常に強く跳ねる曲でも 7 色がまんべんなく出る）。
/// 鮮やかさは常に最大で、明るさは音の大きさに合わせる。
/// 色相と明るさはなめらかにつなぐ。I/O を持たず、時刻は渡された sample 数だけで進む。
/// </summary>
public sealed class G13AudioBacklightEffect
{
    public const int BandCount = 7;
    public const int WindowLength = 2048;

    /// <summary>帯の下端と上端（Hz）。この間を対数で 7 等分する。</summary>
    public const double LowestFrequencyHz = 40;
    public const double HighestFrequencyHz = 16000;

    /// <summary>無音でも LCD の表示が読めるように残す明るさ。</summary>
    public const double BrightnessFloor = 0.15;

    /// <summary>虹の 7 色（赤・橙・黄・緑・青・藍・紫）の色相。帯の低い方から順に割り当てる。</summary>
    private static readonly double[] BandHueDegrees = [0, 28, 60, 120, 195, 240, 285];

    private const double UsualSeconds = 3.0;
    private const double ProminenceSmoothingSeconds = 0.06;
    private const double DominantSwitchMargin = 0.3;
    private const double MinimumUsualDeviation = 0.3;
    private const double NegligibleEnergyShare = 0.001;
    private const double EnergyFloor = 1e-6;
    private const double HueSmoothingSeconds = 0.15;
    private const double BrightnessAttackSeconds = 0.04;
    private const double BrightnessReleaseSeconds = 0.3;
    private const double LoudnessReferenceDecaySeconds = 8.0;
    private const double LoudnessRangeDecibels = 18.0;
    private const double MinimumLoudnessReference = 0.003;

    private readonly int sampleRate;
    private readonly float[] ring = new float[WindowLength];
    private readonly double[] window = new double[WindowLength];
    private readonly double[] real = new double[WindowLength];
    private readonly double[] imaginary = new double[WindowLength];
    private readonly int[] bandFirstBin = new int[BandCount];
    private readonly int[] bandEndBin = new int[BandCount];
    private readonly double[] bandAverage = new double[BandCount];
    private readonly double[] usualLevel = new double[BandCount];
    private readonly double[] usualVariance = new double[BandCount];
    private readonly double[] prominence = new double[BandCount];
    private int ringPosition;
    private double elapsedSeconds;
    private double loudnessReference = MinimumLoudnessReference;
    private double hueDegrees = BandHueDegrees[0];
    private double brightness = BrightnessFloor;

    public G13AudioBacklightEffect(int sampleRate)
    {
        if (sampleRate < 2 * HighestFrequencyHz)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate),
                $"sample rate は {2 * HighestFrequencyHz} Hz 以上が必要です。実際: {sampleRate}");
        }

        this.sampleRate = sampleRate;
        for (var index = 0; index < WindowLength; index++)
        {
            window[index] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * index / (WindowLength - 1));
        }

        var binWidth = (double)sampleRate / WindowLength;
        var ratio = Math.Pow(HighestFrequencyHz / LowestFrequencyHz, 1.0 / BandCount);
        for (var band = 0; band < BandCount; band++)
        {
            var low = LowestFrequencyHz * Math.Pow(ratio, band);
            var high = low * ratio;
            bandFirstBin[band] = (int)Math.Ceiling(low / binWidth);
            bandEndBin[band] = Math.Max(bandFirstBin[band] + 1, (int)Math.Ceiling(high / binWidth));
        }

        Color = ToColor(hueDegrees, brightness);
    }

    /// <summary>いま出している色。</summary>
    public G13BacklightColor Color { get; private set; }

    /// <summary>いちばん目立っている帯（0 が最も低い）。</summary>
    public int DominantBand { get; private set; }

    /// <summary>明るさ（0〜1）。</summary>
    public double Brightness => brightness;

    /// <summary>帯が普段どれだけ鳴っているか（全帯の合計に対する割合）。調整と診断に使う。</summary>
    public double UsualShare(int band)
    {
        var total = bandAverage.Sum();
        return total <= 0 ? 0 : bandAverage[band] / total;
    }

    /// <summary>帯の中心周波数（Hz）。</summary>
    public static double BandCenterFrequencyHz(int band)
    {
        var ratio = Math.Pow(HighestFrequencyHz / LowestFrequencyHz, 1.0 / BandCount);
        return LowestFrequencyHz * Math.Pow(ratio, band + 0.5);
    }

    /// <summary>帯に割り当てた色相（度）。</summary>
    public static double BandHue(int band) => BandHueDegrees[band];

    /// <summary>新しく届いた mono の音を取り込み、その長さだけ時刻を進めて色を返す。</summary>
    public G13BacklightColor Process(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return Color;
        }

        foreach (var sample in samples)
        {
            ring[ringPosition] = sample;
            ringPosition = (ringPosition + 1) % WindowLength;
        }

        var seconds = (double)samples.Length / sampleRate;
        elapsedSeconds += seconds;

        Span<double> energy = stackalloc double[BandCount];
        var level = UpdateLoudnessLevel(Analyze(energy), seconds);
        UpdateDominantBand(energy, level, seconds);
        UpdateBrightness(level, seconds);
        hueDegrees = MoveHue(hueDegrees, BandHueDegrees[DominantBand], Blend(seconds, HueSmoothingSeconds));
        Color = ToColor(hueDegrees, brightness);
        return Color;
    }

    /// <summary>直近の区間の帯ごとの energy を求め、全体の大きさ（RMS）を返す。</summary>
    private double Analyze(Span<double> energy)
    {
        var sumOfSquares = 0.0;
        for (var index = 0; index < WindowLength; index++)
        {
            var sample = ring[(ringPosition + index) % WindowLength];
            sumOfSquares += sample * sample;
            real[index] = sample * window[index];
            imaginary[index] = 0;
        }

        Transform(real, imaginary);
        for (var band = 0; band < BandCount; band++)
        {
            var sum = 0.0;
            for (var bin = bandFirstBin[band]; bin < bandEndBin[band]; bin++)
            {
                sum += real[bin] * real[bin] + imaginary[bin] * imaginary[bin];
            }

            energy[band] = sum;
        }

        return Math.Sqrt(sumOfSquares / WindowLength);
    }

    /// <summary>
    /// 直近でいちばん大きかった音を基準にして、そこからの下がり幅を 0〜1 で返す（音量設定に依らない）。
    /// </summary>
    private double UpdateLoudnessLevel(double loudness, double seconds)
    {
        loudnessReference = Math.Max(
            MinimumLoudnessReference,
            Math.Max(loudness, loudnessReference * Math.Exp(-seconds / LoudnessReferenceDecaySeconds)));
        return loudness <= 0
            ? 0
            : Math.Clamp(1 + 20 * Math.Log10(loudness / loudnessReference) / LoudnessRangeDecibels, 0, 1);
    }

    private void UpdateDominantBand(ReadOnlySpan<double> energy, double level, double seconds)
    {
        // 普段の値が育つまでは経過時間ぶんの平均にする（起動直後に最初の音だけを過大に見ない）。
        var usualBlend = Math.Min(1.0, seconds / Math.Min(UsualSeconds, elapsedSeconds));
        var prominenceBlend = Blend(seconds, ProminenceSmoothingSeconds);
        var usualTotal = 0.0;
        for (var band = 0; band < BandCount; band++)
        {
            bandAverage[band] += (energy[band] - bandAverage[band]) * usualBlend;
            usualTotal += bandAverage[band];
        }

        var first = elapsedSeconds <= seconds;
        var top = -1;
        for (var band = 0; band < BandCount; band++)
        {

            // 帯の大きさを対数で見て、普段の値からのずれを普段の揺れ幅で割る。
            var current = Math.Log(energy[band] + EnergyFloor);
            if (first)
            {
                usualLevel[band] = current;
            }

            var deviation = current - usualLevel[band];
            var usualDeviation = Math.Max(MinimumUsualDeviation, Math.Sqrt(usualVariance[band]));
            prominence[band] += (deviation / usualDeviation - prominence[band]) * prominenceBlend;
            usualLevel[band] += deviation * usualBlend;
            usualVariance[band] += (deviation * deviation - usualVariance[band]) * usualBlend;

            // 普段の全体の大きさに対して無視できるほどしか鳴っていない帯は候補にしない。
            if (energy[band] >= usualTotal * NegligibleEnergyShare &&
                (top < 0 || prominence[band] > prominence[top]))
            {
                top = band;
            }
        }

        // 音がほぼ無い間は、どの帯も目立っていないので色を保つ。
        if (level > 0 && top >= 0 && prominence[top] > prominence[DominantBand] + DominantSwitchMargin)
        {
            DominantBand = top;
        }
    }

    private void UpdateBrightness(double level, double seconds)
    {
        var target = BrightnessFloor + (1 - BrightnessFloor) * level;
        var blend = Blend(seconds, target > brightness ? BrightnessAttackSeconds : BrightnessReleaseSeconds);
        brightness += (target - brightness) * blend;
    }

    private static double Blend(double seconds, double timeConstantSeconds) =>
        1 - Math.Exp(-seconds / timeConstantSeconds);

    /// <summary>色相を近い回り方で目標へ寄せる（途中も鮮やかさは最大のまま）。</summary>
    private static double MoveHue(double current, double target, double blend)
    {
        var difference = (target - current + 540) % 360 - 180;
        return (current + difference * blend + 360) % 360;
    }

    private static G13BacklightColor ToColor(double hue, double value)
    {
        var sector = hue / 60 % 6;
        var rising = sector - Math.Floor(sector);
        var (red, green, blue) = (int)sector switch
        {
            0 => (1.0, rising, 0.0),
            1 => (1 - rising, 1.0, 0.0),
            2 => (0.0, 1.0, rising),
            3 => (0.0, 1 - rising, 1.0),
            4 => (rising, 0.0, 1.0),
            _ => (1.0, 0.0, 1 - rising),
        };
        return new G13BacklightColor(
            (byte)Math.Round(255 * value * red),
            (byte)Math.Round(255 * value * green),
            (byte)Math.Round(255 * value * blue));
    }

    /// <summary>長さが 2 の冪の列をその場で離散 Fourier 変換する（radix-2）。</summary>
    private static void Transform(double[] real, double[] imaginary)
    {
        var length = real.Length;
        for (int index = 1, reversed = 0; index < length; index++)
        {
            var bit = length >> 1;
            for (; (reversed & bit) != 0; bit >>= 1)
            {
                reversed ^= bit;
            }

            reversed ^= bit;
            if (index < reversed)
            {
                (real[index], real[reversed]) = (real[reversed], real[index]);
                (imaginary[index], imaginary[reversed]) = (imaginary[reversed], imaginary[index]);
            }
        }

        for (var size = 2; size <= length; size <<= 1)
        {
            var angle = -2 * Math.PI / size;
            var stepReal = Math.Cos(angle);
            var stepImaginary = Math.Sin(angle);
            for (var start = 0; start < length; start += size)
            {
                var twiddleReal = 1.0;
                var twiddleImaginary = 0.0;
                for (var offset = 0; offset < size / 2; offset++)
                {
                    var even = start + offset;
                    var odd = even + size / 2;
                    var oddReal = real[odd] * twiddleReal - imaginary[odd] * twiddleImaginary;
                    var oddImaginary = real[odd] * twiddleImaginary + imaginary[odd] * twiddleReal;
                    real[odd] = real[even] - oddReal;
                    imaginary[odd] = imaginary[even] - oddImaginary;
                    real[even] += oddReal;
                    imaginary[even] += oddImaginary;
                    (twiddleReal, twiddleImaginary) = (
                        twiddleReal * stepReal - twiddleImaginary * stepImaginary,
                        twiddleReal * stepImaginary + twiddleImaginary * stepReal);
                }
            }
        }
    }
}
