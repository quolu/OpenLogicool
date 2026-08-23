using OpenLogicool.Input;

namespace OpenLogicool.Host;

public sealed class SerialHidDiscoveryException(string message) : Exception(message);

public sealed record SerialHidSessionSelection(
    SerialHidCandidate Candidate,
    SerialHidReadyInfo ReadyInfo,
    SerialHidResidentOutputSession Session);

public sealed record SerialHidConnectionTestResult(
    bool Success,
    string StatusLine,
    SerialHidCandidate? Candidate,
    SerialHidReadyInfo? ReadyInfo);

/// <summary>SetupAPI候補をprotocol HELLO/READYで絞り、0台／複数台を黙って選ばない。</summary>
public sealed class SerialHidDiscoveryService(
    ISerialHidCandidateEnumerator candidates,
    ISerialHidExchangeFactory exchangeFactory)
{
    public static readonly SerialHidSemanticVersion HostVersion = new(1, 0, 0);
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromMilliseconds(80);

    public IReadOnlyList<SerialHidCandidate> ListCandidates() => candidates.EnumerateCandidates();

    public SerialHidSessionSelection Resolve(string? selectedDeviceInstanceId)
    {
        var allCandidates = candidates.EnumerateCandidates();
        IReadOnlyList<SerialHidCandidate> eligible = selectedDeviceInstanceId is null
            ? allCandidates
            : allCandidates.Where(candidate => string.Equals(
                candidate.DeviceInstanceId,
                selectedDeviceInstanceId,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (eligible.Count == 0)
        {
            throw new SerialHidDiscoveryException(selectedDeviceInstanceId is null
                ? "SparkFun Pro MicroのCDC serial候補が見つかりません。USB接続とfirmwareを確認してください。"
                : "保存済みのSparkFun Pro Microが見つかりません。接続を確認するか、出力設定で選び直してください。");
        }

        var successes = new List<SerialHidSessionSelection>();
        var failures = new List<(SerialHidCandidate Candidate, Exception Error)>();
        foreach (var candidate in eligible)
        {
            ISerialHidFrameExchange? exchange = null;
            try
            {
                exchange = exchangeFactory.Open(candidate);
                var protocol = SerialHidProtocolSession.Connect(exchange, HostVersion, RequestTimeout);
                successes.Add(new SerialHidSessionSelection(
                    candidate,
                    protocol.ReadyInfo,
                    new SerialHidResidentOutputSession(
                        exchange,
                        protocol,
                        SerialHidResidentOutputSession.DefaultHeartbeatInterval)));
                exchange = null; // sessionへownership transfer
            }
            catch (Exception exception)
            {
                failures.Add((candidate, exception));
            }
            finally
            {
                exchange?.Dispose();
            }
        }

        if (successes.Count == 1)
        {
            return successes[0];
        }

        foreach (var success in successes)
        {
            success.Session.Dispose();
        }

        if (successes.Count > 1)
        {
            throw new SerialHidDiscoveryException(
                $"OpenLogicool Serial HID firmwareが{successes.Count}台応答しました。出力設定で使う1台を選んでください。");
        }

        var detail = failures.Count == 0
            ? string.Empty
            : "\n" + string.Join("\n", failures.Select(failure =>
                $"{failure.Candidate.DisplayName}: {failure.Error.Message}"));
        var summary = failures.Any(failure => IsVersionMismatch(failure.Error))
            ? "CDC serial候補のprotocol versionがv1と一致しませんでした。"
            : "CDC serial候補はありましたがprotocol v1 handshakeが成立しませんでした。";
        throw new SerialHidDiscoveryException(summary + detail);
    }

    public SerialHidConnectionTestResult Test(string selectedDeviceInstanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedDeviceInstanceId);
        try
        {
            var selection = Resolve(selectedDeviceInstanceId);
            using (selection.Session)
            {
                selection.Session.Start();
                selection.Session.Stop();
            }

            return new SerialHidConnectionTestResult(
                true,
                $"接続確認済み: {selection.Candidate.DisplayName} / firmware {selection.ReadyInfo.FirmwareVersion.Major}.{selection.ReadyInfo.FirmwareVersion.Minor}.{selection.ReadyInfo.FirmwareVersion.Patch}",
                selection.Candidate,
                selection.ReadyInfo);
        }
        catch (Exception exception)
        {
            return new SerialHidConnectionTestResult(false, $"接続できません: {exception.Message}", null, null);
        }
    }

    private static bool IsVersionMismatch(Exception exception) =>
        exception is SerialHidProtocolException { FaultCode: SerialHidFaultCode.UnsupportedVersion }
        || exception.InnerException is not null && IsVersionMismatch(exception.InnerException);
}

public static class ResidentOutputSessionFactory
{
    public static Func<IResidentOutputSession> Create(
        SerialHidOutputSettings settings,
        string watchdogExePath,
        SerialHidDiscoveryService discovery)
    {
        settings.Validate();
        return settings.RequestedRoute switch
        {
            ResidentOutputRoute.SendInput => () => new SendInputResidentOutputSession(watchdogExePath),
            ResidentOutputRoute.SerialHid => () => new DeferredSerialHidResidentOutputSession(
                discovery,
                settings.SelectedDeviceInstanceId),
            _ => throw new ArgumentOutOfRangeException(nameof(settings)),
        };
    }

    private sealed class DeferredSerialHidResidentOutputSession(
        SerialHidDiscoveryService discovery,
        string? selectedDeviceInstanceId) : IResidentOutputSession
    {
        private SerialHidResidentOutputSession? _inner;
        private bool _startAttempted;
        private bool _disposed;

        public ResidentOutputRoute Route => ResidentOutputRoute.SerialHid;

        public IOutputEmitter Emitter =>
            _inner?.Emitter ?? throw new InvalidOperationException("Serial HID output sessionは未起動です。");

        public Exception? BackgroundFailure => _inner?.BackgroundFailure;

        public void Start()
        {
            if (_startAttempted || _disposed)
            {
                throw new InvalidOperationException("Serial HID output sessionは一度しか起動できません。");
            }

            _startAttempted = true;
            var session = discovery.Resolve(selectedDeviceInstanceId).Session;
            try
            {
                session.Start();
                _inner = session;
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }

        public void Stop() => _inner?.Stop();

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _inner?.Dispose();
        }
    }
}
