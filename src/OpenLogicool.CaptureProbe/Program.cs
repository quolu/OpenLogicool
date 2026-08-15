using OpenLogicool.CaptureProbe;

// OpenLogicool Phase 0 / capture probe: WGC / Desktop Duplication / GDI の read-only 検証器。
// device write・システム設定変更はしない。backend 間の fallback はしない。

var command = args.Length > 0 ? args[0] : "";

return command switch
{
    "gdi" => Execute("gdi", GdiCapture.Run),
    "dup" => Execute("dup", DuplicationCapture.Run),
    "wgc-monitor" => Execute("wgc-monitor", WgcMonitorCapture.Run),
    "wgc-window" => Execute("wgc-window", fb => WgcWindowCapture.Run(args.Length > 1 ? args[1] : "", fb)),
    _ => Fail($"unknown command: {command}. available: gdi, dup, wgc-monitor, wgc-window <windowTitleSubstring>"),
};

static int Execute(string command, Func<string, CaptureResult> run)
{
    var fileBase = ProbeOutput.NewFileBase(command);
    var result = run(fileBase);
    return ProbeOutput.WriteAndReport(result, fileBase);
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
