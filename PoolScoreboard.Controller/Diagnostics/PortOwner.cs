using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PoolScoreboard.Controller.Diagnostics;

/// <summary>
/// Names the process listening on a local TCP port, so a failed overlay start can say what owns
/// 127.0.0.1:51234 instead of only "address already in use". Windows exposes this through
/// <c>GetExtendedTcpTable</c>; there is no managed API that maps a port to a process.
/// </summary>
internal static class PortOwner
{
    private const int AddressFamilyInterNetwork = 2;
    private const int TcpTableOwnerPidListener = 3;
    private const uint ErrorInsufficientBuffer = 122;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr table,
        ref int size,
        bool sort,
        int addressFamily,
        int tableClass,
        uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint OwningProcessId;
    }

    /// <summary>
    /// "PoolScoreboard.Controller (PID 21940)" for a listening port, or null when nothing listens on it or the
    /// table cannot be read. Never throws: it exists to make a diagnostic message better.
    /// </summary>
    public static string? Describe(int port) => FindOwningProcessId(port) is { } pid ? NameOf(pid) : null;

    /// <summary>
    /// The process id listening on <paramref name="port"/>, or null. IPv4 only, which is where the overlay
    /// binds; a port with no listener is simply not in this table.
    /// </summary>
    public static int? FindOwningProcessId(int port)
    {
        try
        {
            var size = 0;
            var result = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AddressFamilyInterNetwork, TcpTableOwnerPidListener, 0);
            if (result != ErrorInsufficientBuffer && result != 0)
                return null;

            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                result = GetExtendedTcpTable(buffer, ref size, false, AddressFamilyInterNetwork, TcpTableOwnerPidListener, 0);
                if (result != 0)
                    return null;

                var rows = Marshal.ReadInt32(buffer);
                var rowSize = Marshal.SizeOf<TcpRowOwnerPid>();
                for (var index = 0; index < rows; index++)
                {
                    var row = Marshal.PtrToStructure<TcpRowOwnerPid>(
                        IntPtr.Add(buffer, sizeof(int) + index * rowSize));

                    if (PortOf(row.LocalPort) == port)
                        return (int)row.OwningProcessId;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException
                                             or EntryPointNotFoundException
                                             or OutOfMemoryException)
        {
            // Diagnostics must never be the reason a start fails.
        }

        return null;
    }

    /// <summary>The port arrives as a DWORD holding the two network-order bytes of its low word.</summary>
    private static int PortOf(uint localPort) =>
        (int)(((localPort & 0xFF) << 8) | ((localPort >> 8) & 0xFF));

    private static string NameOf(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return $"{process.ProcessName} (PID {pid})";
        }
        catch (Exception exception) when (exception is ArgumentException
                                             or InvalidOperationException
                                             or Win32Exception)
        {
            // The owner exited between the lookup and opening it; the id is still worth printing.
            return $"PID {pid}";
        }
    }
}
