using OpenLogicool.Devices.G13;
using Xunit;

namespace OpenLogicool.Devices.G13.Tests;

public sealed class G13AudioBacklightEffectTests
{
    private const int SampleRate = 48000;
    private const int TickSamples = SampleRate * 30 / 1000;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void A_tone_in_one_band_brings_out_the_rainbow_color_of_that_band(int band)
    {
        var effect = new G13AudioBacklightEffect(SampleRate);
        var signal = new Signal();
        Feed(effect, signal, 0.3);

        signal.Amplitudes[band] = 0.5;
        var colors = Feed(effect, signal, 1.5);

        Assert.Equal(band, effect.DominantBand);
        Assert.InRange(HueDistance(Hue(colors[^1]), G13AudioBacklightEffect.BandHue(band)), 0, 3);
    }

    [Fact]
    public void Every_band_gets_its_turn_even_when_the_bass_is_always_the_loudest()
    {
        var effect = new G13AudioBacklightEffect(SampleRate);
        var signal = new Signal();
        for (var band = 0; band < G13AudioBacklightEffect.BandCount; band++)
        {
            // 低い帯ほど強い（1帯ごとに 4 dB 下がる）。
            signal.Amplitudes[band] = 0.3 * Math.Pow(10, -4.0 * band / 20);
        }

        Feed(effect, signal, 3.0);
        for (var band = G13AudioBacklightEffect.BandCount - 1; band >= 0; band--)
        {
            var usual = signal.Amplitudes[band];
            signal.Amplitudes[band] = usual * 3;
            Feed(effect, signal, 0.4);
            Assert.Equal(band, effect.DominantBand);
            signal.Amplitudes[band] = usual;
            Feed(effect, signal, 0.2);
        }
    }

    [Fact]
    public void Color_is_always_fully_saturated_and_never_darker_than_the_floor()
    {
        var effect = new G13AudioBacklightEffect(SampleRate);
        var random = new Random(20261010);
        var signal = new Signal();
        var floor = (int)Math.Floor(255 * G13AudioBacklightEffect.BrightnessFloor);
        for (var round = 0; round < 60; round++)
        {
            for (var band = 0; band < G13AudioBacklightEffect.BandCount; band++)
            {
                signal.Amplitudes[band] = random.Next(4) == 0 ? 0 : random.NextDouble() * 0.2;
            }

            foreach (var color in Feed(effect, signal, 0.15))
            {
                Assert.Equal(0, Math.Min(color.Red, Math.Min(color.Green, color.Blue)));
                Assert.True(Math.Max(color.Red, Math.Max(color.Green, color.Blue)) >= floor);
            }
        }
    }

    [Fact]
    public void Brightness_follows_loudness_and_silence_keeps_the_color_at_the_floor()
    {
        var effect = new G13AudioBacklightEffect(SampleRate);
        var signal = new Signal();
        signal.Amplitudes[4] = 0.5;
        Feed(effect, signal, 1.0);
        Assert.InRange(effect.Brightness, 0.95, 1.0);

        // 9 dB 下げる（基準からの幅 18 dB の半分）。
        signal.Amplitudes[4] = 0.5 * Math.Pow(10, -9.0 / 20);
        Feed(effect, signal, 1.0);
        Assert.InRange(effect.Brightness, 0.5, 0.7);

        // 音が自然に消えていく（60 ms かけて下がる）。
        signal.FadeOut(0.06);
        var colors = Feed(effect, signal, 2.5);
        Assert.InRange(effect.Brightness, G13AudioBacklightEffect.BrightnessFloor, G13AudioBacklightEffect.BrightnessFloor + 0.01);
        Assert.Equal(4, effect.DominantBand);
        Assert.InRange(HueDistance(Hue(colors[^1]), G13AudioBacklightEffect.BandHue(4)), 0, 3);
    }

    [Fact]
    public void Brightness_does_not_depend_on_the_volume_setting()
    {
        foreach (var amplitude in new[] { 0.5, 0.05 })
        {
            var effect = new G13AudioBacklightEffect(SampleRate);
            var signal = new Signal();
            signal.Amplitudes[2] = amplitude;
            Feed(effect, signal, 1.0);
            Assert.InRange(effect.Brightness, 0.95, 1.0);
        }
    }

    [Fact]
    public void Color_moves_smoothly_to_the_next_band()
    {
        var effect = new G13AudioBacklightEffect(SampleRate);
        var signal = new Signal();
        signal.Amplitudes[0] = 0.4;
        var before = Feed(effect, signal, 1.0)[^1];

        signal.Amplitudes[0] = 0;
        signal.Amplitudes[3] = 0.4;
        var colors = Feed(effect, signal, 1.0);

        var previous = Hue(before);
        foreach (var color in colors)
        {
            var hue = Hue(color);
            Assert.InRange(HueDistance(hue, previous), 0, 25);
            previous = hue;
        }

        Assert.InRange(HueDistance(previous, G13AudioBacklightEffect.BandHue(3)), 0, 3);
    }

    [Fact]
    public void No_new_sound_does_not_advance_time()
    {
        var effect = new G13AudioBacklightEffect(SampleRate);
        var signal = new Signal();
        signal.Amplitudes[5] = 0.4;
        var color = Feed(effect, signal, 0.2)[^1];

        Assert.Equal(color, effect.Process([]));
        Assert.Equal(color, effect.Color);
    }

    [Fact]
    public void Sample_rate_must_cover_the_highest_band()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new G13AudioBacklightEffect(22050));
    }

    private static List<G13BacklightColor> Feed(G13AudioBacklightEffect effect, Signal signal, double seconds)
    {
        var colors = new List<G13BacklightColor>();
        var buffer = new float[TickSamples];
        for (var tick = 0; tick < (int)Math.Round(seconds * SampleRate / TickSamples); tick++)
        {
            signal.Fill(buffer);
            colors.Add(effect.Process(buffer));
        }

        return colors;
    }

    private static double Hue(G13BacklightColor color)
    {
        double red = color.Red, green = color.Green, blue = color.Blue;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var range = max - min;
        var hue = max == red
            ? 60 * ((green - blue) / range % 6)
            : max == green
                ? 60 * ((blue - red) / range + 2)
                : 60 * ((red - green) / range + 4);
        return (hue + 360) % 360;
    }

    private static double HueDistance(double first, double second) =>
        Math.Abs((first - second + 540) % 360 - 180);

    /// <summary>帯ごとの中心周波数の正弦波を足した音。位相は区間をまたいで続く。</summary>
    private sealed class Signal
    {
        private long position;
        private double gain = 1;
        private double gainStep;

        public double[] Amplitudes { get; } = new double[G13AudioBacklightEffect.BandCount];

        public void FadeOut(double seconds) => gainStep = -1 / (seconds * SampleRate);

        public void Fill(Span<float> destination)
        {
            for (var index = 0; index < destination.Length; index++, position++)
            {
                gain = Math.Clamp(gain + gainStep, 0, 1);
                var sample = 0.0;
                for (var band = 0; band < Amplitudes.Length; band++)
                {
                    sample += Amplitudes[band] * Math.Sin(
                        2 * Math.PI * G13AudioBacklightEffect.BandCenterFrequencyHz(band) * position / SampleRate);
                }

                destination[index] = (float)(sample * gain);
            }
        }
    }
}
