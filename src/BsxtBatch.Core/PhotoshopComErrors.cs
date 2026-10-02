using System.Runtime.InteropServices;

namespace BsxtBatch.Core;

/// <summary>Separates "Photoshop the process is gone or wedged" from an ordinary
/// per-file action error. Only the former is worth killing Photoshop and retrying.</summary>
public static class PhotoshopComErrors
{
    public const int RpcECallRejected = unchecked((int)0x80010001);
    public const int RpcEServerfault = unchecked((int)0x80010105);
    public const int RpcEDisconnected = unchecked((int)0x80010108);
    public const int RpcEServercallRetrylater = unchecked((int)0x8001010A);
    public const int RpcESysCallFailed = unchecked((int)0x80010100);
    public const int CoEObjnotconnected = unchecked((int)0x800401FD);
    public const int RpcSServerUnavailable = unchecked((int)0x800706BA);
    public const int RpcSCallFailed = unchecked((int)0x800706BE);
    public const int RpcSCallFailedDne = unchecked((int)0x800706BF);
    public const int RpcSUnknownIf = unchecked((int)0x800706B5);

    private static readonly HashSet<int> DeathHResults =
    [
        RpcECallRejected,
        RpcEServerfault,
        RpcEDisconnected,
        RpcEServercallRetrylater,
        RpcESysCallFailed,
        CoEObjnotconnected,
        RpcSServerUnavailable,
        RpcSCallFailed,
        RpcSCallFailedDne,
        RpcSUnknownIf,
    ];

    /// <summary>True when <paramref name="ex"/> means the COM server rejected, dropped,
    /// or crashed the call — not when an action step failed on this document.</summary>
    public static bool IsDisconnected(Exception ex)
    {
        if (ex is InvalidComObjectException)
            return true;

        if (ex is not COMException com)
            return false;

        if (IsMissingActionMessage(com.Message))
            return false;

        if (DeathHResults.Contains(com.HResult))
            return true;

        var m = com.Message;
        return m.Contains("application is busy", StringComparison.OrdinalIgnoreCase)
            || m.Contains("call was rejected", StringComparison.OrdinalIgnoreCase)
            || m.Contains("RPC server", StringComparison.OrdinalIgnoreCase)
            || m.Contains("RPC call", StringComparison.OrdinalIgnoreCase)
            || m.Contains("remote procedure call", StringComparison.OrdinalIgnoreCase)
            || m.Contains("disconnected", StringComparison.OrdinalIgnoreCase)
            || m.Contains("not connected", StringComparison.OrdinalIgnoreCase)
            || m.Contains("server execution failed", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when Photoshop reports the BSXT action or its set does not exist.</summary>
    public static bool IsMissingActionMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return false;

        return message.Contains("action", StringComparison.OrdinalIgnoreCase) &&
               (message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("can't find", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("cannot find", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("is not a valid", StringComparison.OrdinalIgnoreCase));
    }
}
