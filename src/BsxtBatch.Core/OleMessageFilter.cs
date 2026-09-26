using System.Runtime.InteropServices;
using System.Threading;

namespace BsxtBatch.Core;

/// <summary>
/// Standard OLE message filter. Photoshop returns RPC_E_SERVERCALL_RETRYLATER while it is
/// mid-operation (long filters, saves, ACR). Without a registered filter the CLR throws
/// "application is busy" and the batch breaks. Register once on the STA thread that drives
/// Photoshop, unregister when done.
/// </summary>
public sealed class OleMessageFilter : IOleMessageFilter
{
    private const int RetryDelayMs = 250;
    private const int MaxRetryMs = 5 * 60 * 1000; // keep retrying up to ~5 min per call

    private static OleMessageFilter? _registered;
    private static int _refCount;
    private static readonly object Gate = new();

    public static void Register()
    {
        lock (Gate)
        {
            if (_refCount++ == 0)
            {
                _registered = new OleMessageFilter();
                _ = CoRegisterMessageFilter(_registered, out _);
            }
        }
    }

    public static void Unregister()
    {
        lock (Gate)
        {
            if (--_refCount <= 0)
            {
                _refCount = 0;
                _ = CoRegisterMessageFilter(null, out _);
                _registered = null;
            }
        }
    }

    int IOleMessageFilter.HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo)
        => 0; // SERVERCALL_DONE

    int IOleMessageFilter.RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType)
        => dwRejectType == 2 // SERVERCALL_RETRYLATER
            ? (dwTickCount < MaxRetryMs ? RetryDelayMs : -1)
            : -1;            // SERVERCALL_REJECTED / cannot retry

    int IOleMessageFilter.MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType)
        => 2; // PENDINGMSG_WAITDEFPROCESS

    [DllImport("ole32.dll")]
    private static extern int CoRegisterMessageFilter(IOleMessageFilter? newFilter, out IOleMessageFilter? oldFilter);
}

/// <remarks>Must be at namespace scope — a nested interface cannot appear in its
/// enclosing class's base list (CS0246/CS0540).</remarks>
[ComVisible(true)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("00000016-0000-0000-C000-000000000046")]
public interface IOleMessageFilter
{
    [PreserveSig] int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo);

    [PreserveSig] int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType);

    [PreserveSig] int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType);
}
