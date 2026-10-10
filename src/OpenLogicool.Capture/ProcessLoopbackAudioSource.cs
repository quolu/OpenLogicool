using System.Runtime.InteropServices;
using OpenLogicool.Contracts.Audio;

namespace OpenLogicool.Capture;

/// <summary>
/// 一つの process と子 process が鳴らしている音だけを拾う（Windows の process loopback）。
/// 対象の process には触れず、Windows の音声エンジンから受け取る。ほかのアプリの音は混ざらない。
/// 作った thread で読み、同じ thread で破棄する。
/// </summary>
public sealed class ProcessLoopbackAudioSource : IProcessAudioSource
{
    private const int Channels = 2;
    private const int BitsPerSample = 16;
    private const int CaptureSampleRate = 48000;
    private const long BufferDuration100Ns = 2_000_000;
    private const string ProcessLoopbackDevice = @"VAD\Process_Loopback";
    private const ushort VariantTypeBlob = 65;
    private const int ActivationTypeProcessLoopback = 1;
    private const int LoopbackModeIncludeProcessTree = 0;
    private const uint StreamFlagsLoopback = 0x0002_0000;
    private const uint StreamFlagsEventCallback = 0x0004_0000;
    private const uint BufferFlagsSilent = 0x2;

    private readonly AutoResetEvent packetReady = new(false);
    private readonly Native.IAudioClient client;
    private readonly Native.IAudioCaptureClient capture;
    private short[] packet = [];
    private bool disposed;

    public ProcessLoopbackAudioSource(int processId)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        client = Activate(processId);
        try
        {
            var format = new Native.WaveFormat
            {
                FormatTag = 1,
                Channels = Channels,
                SamplesPerSecond = CaptureSampleRate,
                AverageBytesPerSecond = CaptureSampleRate * Channels * BitsPerSample / 8,
                BlockAlign = Channels * BitsPerSample / 8,
                BitsPerSample = BitsPerSample,
            };
            var formatPointer = Marshal.AllocHGlobal(Marshal.SizeOf<Native.WaveFormat>());
            try
            {
                Marshal.StructureToPtr(format, formatPointer, false);
                Check(
                    client.Initialize(
                        0,
                        StreamFlagsLoopback | StreamFlagsEventCallback,
                        BufferDuration100Ns,
                        0,
                        formatPointer,
                        IntPtr.Zero),
                    "音の取り込みを初期化できませんでした");
            }
            finally
            {
                Marshal.FreeHGlobal(formatPointer);
            }

            var captureInterface = typeof(Native.IAudioCaptureClient).GUID;
            Check(client.GetService(ref captureInterface, out var service), "音の取り込み口を取得できませんでした");
            capture = (Native.IAudioCaptureClient)service;
            Check(
                client.SetEventHandle(packetReady.SafeWaitHandle.DangerousGetHandle()),
                "音の到着通知を設定できませんでした");
            Check(client.Start(), "音の取り込みを開始できませんでした");
        }
        catch
        {
            Marshal.ReleaseComObject(client);
            packetReady.Dispose();
            throw;
        }
    }

    public int SampleRate => CaptureSampleRate;

    public int Read(Span<float> destination)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var written = 0;
        while (true)
        {
            Check(capture.GetNextPacketSize(out var nextFrames), "音の到着量を取得できませんでした");
            if (nextFrames == 0)
            {
                return written;
            }

            if (destination.Length - written < nextFrames)
            {
                if (written == 0)
                {
                    throw new ArgumentException(
                        $"読み出し先が1回の到着分（{nextFrames} sample）より小さいです。", nameof(destination));
                }

                return written;
            }

            Check(capture.GetBuffer(out var data, out var frames, out var flags, out _, out _), "音を読み出せませんでした");
            var target = destination.Slice(written, (int)frames);
            if ((flags & BufferFlagsSilent) != 0)
            {
                target.Clear();
            }
            else
            {
                var values = (int)frames * Channels;
                if (packet.Length < values)
                {
                    packet = new short[values];
                }

                Marshal.Copy(data, packet, 0, values);
                for (var frame = 0; frame < target.Length; frame++)
                {
                    target[frame] = (packet[frame * 2] + packet[frame * 2 + 1]) / 65536f;
                }
            }

            Check(capture.ReleaseBuffer(frames), "読み出した音を返却できませんでした");
            written += (int)frames;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        _ = client.Stop();
        Marshal.ReleaseComObject(capture);
        Marshal.ReleaseComObject(client);
        packetReady.Dispose();
    }

    private static Native.IAudioClient Activate(int processId)
    {
        var parameters = new Native.ActivationParameters
        {
            ActivationType = ActivationTypeProcessLoopback,
            TargetProcessId = (uint)processId,
            ProcessLoopbackMode = LoopbackModeIncludeProcessTree,
        };
        var parametersSize = Marshal.SizeOf<Native.ActivationParameters>();
        var parametersPointer = Marshal.AllocHGlobal(parametersSize);
        var variantPointer = Marshal.AllocHGlobal(Marshal.SizeOf<Native.BlobVariant>());
        using var handler = new ActivationHandler();
        try
        {
            Marshal.StructureToPtr(parameters, parametersPointer, false);
            Marshal.StructureToPtr(
                new Native.BlobVariant { Type = VariantTypeBlob, Size = (uint)parametersSize, Data = parametersPointer },
                variantPointer,
                false);
            var clientInterface = typeof(Native.IAudioClient).GUID;
            Check(
                Native.ActivateAudioInterfaceAsync(
                    ProcessLoopbackDevice, ref clientInterface, variantPointer, handler, out var operation),
                $"process {processId} の音の取り込みを要求できませんでした");
            if (!handler.Completed.WaitOne(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException($"process {processId} の音の取り込みが5秒以内に始まりませんでした。");
            }

            operation.GetActivateResult(out var result, out var activated);
            Check(result, $"process {processId} の音の取り込みを開始できませんでした");
            return (Native.IAudioClient)activated;
        }
        finally
        {
            Marshal.FreeHGlobal(variantPointer);
            Marshal.FreeHGlobal(parametersPointer);
        }
    }

    private static void Check(int result, string message)
    {
        if (result < 0)
        {
            throw new InvalidOperationException($"{message}（HRESULT 0x{result:X8}）。", Marshal.GetExceptionForHR(result));
        }
    }

    private sealed class ActivationHandler :
        Native.IActivateAudioInterfaceCompletionHandler, Native.IAgileObject, IDisposable
    {
        public ManualResetEvent Completed { get; } = new(false);

        public void ActivateCompleted(Native.IActivateAudioInterfaceAsyncOperation activateOperation) => Completed.Set();

        public void Dispose() => Completed.Dispose();
    }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct ActivationParameters
        {
            public int ActivationType;
            public uint TargetProcessId;
            public int ProcessLoopbackMode;
        }

        /// <summary>PROPVARIANT の VT_BLOB の形。</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct BlobVariant
        {
            public ushort Type;
            public ushort Reserved1;
            public ushort Reserved2;
            public ushort Reserved3;
            public uint Size;
            public IntPtr Data;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct WaveFormat
        {
            public ushort FormatTag;
            public ushort Channels;
            public uint SamplesPerSecond;
            public uint AverageBytesPerSecond;
            public ushort BlockAlign;
            public ushort BitsPerSample;
            public ushort ExtraSize;
        }

        [DllImport("Mmdevapi.dll", ExactSpelling = true)]
        public static extern int ActivateAudioInterfaceAsync(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
            ref Guid interfaceId,
            IntPtr activationParameters,
            IActivateAudioInterfaceCompletionHandler completionHandler,
            out IActivateAudioInterfaceAsyncOperation activationOperation);

        /// <summary>完了通知を任意の thread から受けるための印。</summary>
        [ComImport]
        [Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAgileObject
        {
        }

        [ComImport]
        [Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IActivateAudioInterfaceCompletionHandler
        {
            void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
        }

        [ComImport]
        [Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IActivateAudioInterfaceAsyncOperation
        {
            void GetActivateResult(
                out int activateResult,
                [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
        }

        [ComImport]
        [Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioClient
        {
            [PreserveSig]
            int Initialize(
                int shareMode,
                uint streamFlags,
                long bufferDuration,
                long periodicity,
                IntPtr format,
                IntPtr audioSessionGuid);

            [PreserveSig]
            int GetBufferSize(out uint bufferFrames);

            [PreserveSig]
            int GetStreamLatency(out long latency);

            [PreserveSig]
            int GetCurrentPadding(out uint paddingFrames);

            [PreserveSig]
            int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);

            [PreserveSig]
            int GetMixFormat(out IntPtr deviceFormat);

            [PreserveSig]
            int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);

            [PreserveSig]
            int Start();

            [PreserveSig]
            int Stop();

            [PreserveSig]
            int Reset();

            [PreserveSig]
            int SetEventHandle(IntPtr eventHandle);

            [PreserveSig]
            int GetService(ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object service);
        }

        [ComImport]
        [Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioCaptureClient
        {
            [PreserveSig]
            int GetBuffer(
                out IntPtr data,
                out uint frames,
                out uint flags,
                out ulong devicePosition,
                out ulong performanceCounterPosition);

            [PreserveSig]
            int ReleaseBuffer(uint frames);

            [PreserveSig]
            int GetNextPacketSize(out uint frames);
        }
    }
}
