using QadAutomation.Core.Configuration;
using QadAutomation.Core.Tickets;
using QadAutomation.Core.Transfer;
using QadAutomation.Core.Vpn;

namespace QadAutomation.Cli;

/// <summary>
/// Process exit codes.
/// </summary>
/// <remarks>
/// Distinct codes per failure class so the tool can eventually be called from a
/// script that needs to tell "you typed it wrong" apart from "the deploy failed".
/// </remarks>
public static class ExitCode
{
    public const int Ok = 0;
    public const int ConfigurationError = 1;
    public const int UsageError = 2;
    public const int TicketError = 3;

    /// <summary>
    /// The VPN could not be established. Its own code because it is the one
    /// failure a caller might sensibly retry: the config is fine and the files
    /// are fine, the network was not.
    /// </summary>
    public const int VpnError = 4;

    /// <summary>
    /// The files could not be transferred. Separate from <see cref="VpnError"/>
    /// because the two call for different responses: a VPN failure means try
    /// again, a transfer failure usually means a path or a permission is wrong.
    /// </summary>
    public const int TransferError = 5;

    /// <summary>
    /// At least one program did not compile. Distinct from
    /// <see cref="TransferError"/> because the files arrived intact and the
    /// connection was fine - the source is wrong, and the fix is in the program,
    /// not in the tool or the config.
    /// </summary>
    public const int CompileError = 6;

    public const int Unexpected = 99;

    /// <summary>
    /// Whether this is a failure the tool anticipated and can explain.
    /// </summary>
    /// <remarks>
    /// The line between "your VPN is down" and "this tool has a bug". Everything
    /// on this side gets a sentence; everything else gets a stack trace, because
    /// for those the stack trace is the useful part.
    /// </remarks>
    public static bool IsExpected(Exception exception) => exception is
        ConfigurationException or TicketFolderException or VpnException or TransferException;

    /// <summary>
    /// The code for an anticipated failure.
    /// </summary>
    /// <remarks>
    /// Here rather than inline in the dispatcher because two callers need to
    /// agree: the top-level handler, and the guided flow, which catches these
    /// itself so that a forgotten VPN costs a retry rather than a relaunch.
    /// </remarks>
    public static int For(Exception exception) => exception switch
    {
        ConfigurationException => ConfigurationError,
        TicketFolderException => TicketError,
        VpnException => VpnError,
        TransferException => TransferError,
        _ => Unexpected
    };
}
