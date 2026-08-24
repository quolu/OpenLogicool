using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;

namespace OpenLogicool.Probe;

internal sealed class OwnedTcpConnectionObserver : IAsyncDisposable
{
    private const int AddressFamilyInet = 2;
    private const int AddressFamilyInet6 = 23;
    private const int TcpTableOwnerPidAll = 5;
    private const int ErrorInsufficientBuffer = 122;

    private readonly HashSet<int> processIds;
    private readonly ConcurrentDictionary<string, OwnedTcpConnection> observations = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task worker;

    public OwnedTcpConnectionObserver(IEnumerable<int> processIds)
    {
        this.processIds = processIds.ToHashSet();
        if (this.processIds.Count == 0)
        {
            throw new ArgumentException("network観測対象processが必要です。", nameof(processIds));
        }

        Capture();
        worker = Task.Run(ObserveAsync);
    }

    public IReadOnlyList<OwnedTcpConnection> Observations => observations.Values
        .OrderBy(item => item.ProcessId)
        .ThenBy(item => item.LocalAddress, StringComparer.Ordinal)
        .ThenBy(item => item.LocalPort)
        .ThenBy(item => item.RemoteAddress, StringComparer.Ordinal)
        .ThenBy(item => item.RemotePort)
        .ToArray();

    public bool HasNonLoopbackEstablished => Observations.Any(item =>
        item.State == "Established"
        && !IPAddress.IsLoopback(IPAddress.Parse(item.RemoteAddress)));

    public async ValueTask DisposeAsync()
    {
        await cancellation.CancelAsync();
        await worker.ConfigureAwait(false);
        cancellation.Dispose();
    }

    private async Task ObserveAsync()
    {
        while (!cancellation.IsCancellationRequested)
        {
            Capture();
            try
            {
                await Task.Delay(10, cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void Capture()
    {
        foreach (var connection in ReadIpv4().Concat(ReadIpv6()))
        {
            if (!processIds.Contains(connection.ProcessId)
                || connection.State is not ("Listen" or "Established"))
            {
                continue;
            }

            var key = $"{connection.ProcessId}|{connection.State}|{connection.LocalAddress}|{connection.LocalPort}|{connection.RemoteAddress}|{connection.RemotePort}";
            observations.TryAdd(key, connection);
        }
    }

    private static IEnumerable<OwnedTcpConnection> ReadIpv4() => ReadTable<MibTcpRowOwnerPid>(
        AddressFamilyInet,
        row => new OwnedTcpConnection(
            checked((int)row.OwningPid),
            StateName(row.State),
            new IPAddress(row.LocalAddress).ToString(),
            Port(row.LocalPort),
            new IPAddress(row.RemoteAddress).ToString(),
            Port(row.RemotePort)));

    private static IEnumerable<OwnedTcpConnection> ReadIpv6() => ReadTable<MibTcp6RowOwnerPid>(
        AddressFamilyInet6,
        row => new OwnedTcpConnection(
            checked((int)row.OwningPid),
            StateName(row.State),
            new IPAddress(row.LocalAddress, row.LocalScopeId).ToString(),
            Port(row.LocalPort),
            new IPAddress(row.RemoteAddress, row.RemoteScopeId).ToString(),
            Port(row.RemotePort)));

    private static IEnumerable<OwnedTcpConnection> ReadTable<TRow>(
        int addressFamily,
        Func<TRow, OwnedTcpConnection> convert)
        where TRow : struct
    {
        var size = 0;
        var first = GetExtendedTcpTable(
            IntPtr.Zero,
            ref size,
            true,
            addressFamily,
            TcpTableOwnerPidAll,
            0);
        if (first != ErrorInsufficientBuffer)
        {
            throw new InvalidOperationException($"GetExtendedTcpTable size failed: {first}");
        }

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var result = GetExtendedTcpTable(
                buffer,
                ref size,
                true,
                addressFamily,
                TcpTableOwnerPidAll,
                0);
            if (result != 0)
            {
                throw new InvalidOperationException($"GetExtendedTcpTable failed: {result}");
            }

            var count = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<TRow>();
            var rows = new List<OwnedTcpConnection>(count);
            var current = IntPtr.Add(buffer, sizeof(uint));
            for (var index = 0; index < count; index++)
            {
                rows.Add(convert(Marshal.PtrToStructure<TRow>(current)));
                current = IntPtr.Add(current, rowSize);
            }

            return rows;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int Port(uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        return bytes[0] << 8 | bytes[1];
    }

    private static string StateName(uint state) => state switch
    {
        2 => "Listen",
        5 => "Established",
        _ => $"State{state}",
    };

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(
        IntPtr table,
        ref int size,
        bool order,
        int addressFamily,
        int tableClass,
        uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddress;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddress;
        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint OwningPid;
    }
}

internal sealed record OwnedTcpConnection(
    int ProcessId,
    string State,
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort);
