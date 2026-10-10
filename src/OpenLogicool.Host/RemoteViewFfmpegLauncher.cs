using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using OpenLogicool.Capture;

namespace OpenLogicool.Host;

/// <summary>
/// 実物の ffmpeg を窓を出さずに起動する。Job Object（閉じたら kill）へ入れ、Host が落ちても配信だけが続かないようにする。
/// </summary>
public sealed class RemoteViewFfmpegLauncher : IDisposable
{
    public const string NotFoundMessage = "ffmpeg が見つかりません。winget install Gyan.FFmpeg で導入してください。";

    private readonly object gate = new();
    private KillOnCloseJob? job;

    public IRemoteViewEncoderProcess Launch(IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(ResolveFfmpeg())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg を起動できませんでした。");
        try
        {
            lock (gate)
            {
                (job ??= new KillOnCloseJob()).Add(process);
            }
        }
        catch
        {
            process.Kill();
            process.Dispose();
            throw;
        }

        return new FfmpegEncoderProcess(process);
    }

    public void Dispose()
    {
        lock (gate)
        {
            job?.Dispose();
            job = null;
        }
    }

    /// <summary>PATH から ffmpeg.exe を探す。見つからなければ明示のエラーにする。</summary>
    private static string ResolveFfmpeg()
    {
        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = entry.Trim().Trim('"');
            if (directory.Length == 0)
            {
                continue;
            }

            var candidate = Path.Combine(directory, "ffmpeg.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(NotFoundMessage);
    }

    private sealed class FfmpegEncoderProcess(Process process) : IRemoteViewEncoderProcess
    {
        public Stream StandardInput => process.StandardInput.BaseStream;

        public bool HasExited => process.HasExited;

        public int? ExitCode => process.HasExited ? process.ExitCode : null;

        /// <summary>進捗は \r、その他は \n で区切られる。</summary>
        public IEnumerable<string> ReadErrorLines()
        {
            var reader = process.StandardError;
            var line = new StringBuilder();
            int value;
            while ((value = reader.Read()) >= 0)
            {
                if (value is '\r' or '\n')
                {
                    if (line.Length > 0)
                    {
                        yield return line.ToString();
                        line.Clear();
                    }
                }
                else
                {
                    line.Append((char)value);
                }
            }

            if (line.Length > 0)
            {
                yield return line.ToString();
            }
        }

        public void CloseStandardInput() => process.StandardInput.Close();

        public bool WaitForExit(TimeSpan timeout) => process.WaitForExit((int)timeout.TotalMilliseconds);

        public void Kill()
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // 確かめた直後に終了した。目的は達している。
            }
        }

        public void Dispose() => process.Dispose();
    }

    /// <summary>Host が落ちても ffmpeg が残らないよう、Job Object（閉じたら kill）へ入れる。</summary>
    private sealed class KillOnCloseJob : IDisposable
    {
        private const int ExtendedLimitInformationClass = 9;
        private const uint KillOnJobClose = 0x2000;

        private IntPtr handle;

        public KillOnCloseJob()
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            if (handle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Job Object を作れませんでした。");
            }

            var info = new ExtendedLimitInformation();
            info.Basic.LimitFlags = KillOnJobClose;
            var size = Marshal.SizeOf<ExtendedLimitInformation>();
            var pointer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, pointer, false);
                if (!SetInformationJobObject(handle, ExtendedLimitInformationClass, pointer, (uint)size))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Job Object に終了時 kill を設定できませんでした。");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }

        public void Add(Process process)
        {
            if (!AssignProcessToJobObject(handle, process.Handle))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ffmpeg を Job Object へ入れられませんでした。");
            }
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero)
            {
                CloseHandle(handle);
                handle = IntPtr.Zero;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public nuint MinimumWorkingSetSize;
            public nuint MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public nuint Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimitInformation
        {
            public BasicLimitInformation Basic;
            public IoCounters Io;
            public nuint ProcessMemoryLimit;
            public nuint JobMemoryLimit;
            public nuint PeakProcessMemoryUsed;
            public nuint PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(IntPtr job, int informationClass, IntPtr information, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}

/// <summary>process loopback の音源を <see cref="IRemoteViewAudioSource"/> として渡す。</summary>
public sealed class ProcessLoopbackRemoteViewAudioSource(ProcessLoopbackAudioSource source) : IRemoteViewAudioSource
{
    public static IRemoteViewAudioSource Create(int processId) =>
        new ProcessLoopbackRemoteViewAudioSource(new ProcessLoopbackAudioSource(processId));

    public int ReadStereo(Span<short> interleaved) => source.ReadStereo(interleaved);

    public void Dispose() => source.Dispose();
}
