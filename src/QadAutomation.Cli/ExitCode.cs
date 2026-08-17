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

    public const int Unexpected = 99;
}
