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

    public const int Unexpected = 99;
}
