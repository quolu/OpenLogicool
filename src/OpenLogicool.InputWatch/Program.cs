using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace OpenLogicool.InputWatch;

/// <summary>
/// 管理者権限のタスクから起動される監視process。キーボードとマウスの手入力を観測し、Bot本体へ数だけを送る。
/// 管理者権限で動く部分をこのexeだけに限るため、Bot本体のexeやDLLを読み込まず、Bot本体からは何も受け取らない。
/// Bot本体の通信口がつながらない時と、切れた時に終了する。
/// </summary>
internal static class Program
{
    private static int Main(string[] arguments)
    {
        try
        {
            // 導入の手順が、置いた実行ファイルで観測を始められるかを確かめるための入口。何も受け取らず、何も送らない。
            if (arguments is ["--self-test"])
            {
                using var check = new RawUserInputMonitor(BotOutputDevices.IsSerialHidOutput);
                _ = check.Snapshot();
                return 0;
            }
            if (arguments.Length != 0) return 5;
            // 通常権限のBot本体が、このprocessの権限を借りられない水準で開く。
            using var pipe = new NamedPipeClientStream(".", UserInputWatchProtocol.PipeName, PipeDirection.Out, PipeOptions.None,
                TokenImpersonationLevel.Identification);
            pipe.Connect(UserInputWatchProtocol.ConnectTimeoutMs);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 256) { AutoFlush = true };
            using var monitor = new RawUserInputMonitor(BotOutputDevices.IsSerialHidOutput);
            writer.WriteLine(UserInputWatchProtocol.Ready(ProcessIntegrity.Current()));
            while (true)
            {
                writer.WriteLine(UserInputWatchProtocol.Format(monitor.Snapshot()));
                Thread.Sleep(UserInputWatchProtocol.ReportIntervalMs);
            }
        }
        catch (IOException) { return 0; } // Bot本体が通信口を閉じた。
        catch (TimeoutException) { return 4; } // Bot本体の通信口が無い。
        catch (Exception) { return 2; }
    }
}
