using QadAutomation.Core.Configuration;
using QadAutomation.Core.Processes;

namespace QadAutomation.Core.Vpn;

/// <summary>
/// Drives the Windows built-in VPN client through <c>rasdial.exe</c>.
/// </summary>
/// <remarks>
/// <para>
/// This replaces exactly one manual step: opening the system tray, clicking the
/// VPN flyout, picking the connection and pressing Connect. <c>rasdial</c> is the
/// command-line front end to the same RAS phonebook that flyout shows, so the
/// tool connects to precisely the entry the operator already uses. Nothing new is
/// configured on the machine, and the connection remains usable and visible by
/// hand.
/// </para>
/// <para>
/// <b>Credentials are preferably not supplied at all.</b> If the entry was saved
/// with "remember my credentials" - as the pilot site's is - then
/// <c>rasdial "Name"</c> alone connects, and no VPN password needs to exist in
/// <c>.env</c> or anywhere else this tool can read. Supplying a username and
/// password is supported for machines where the entry was not saved, at the cost
/// described on <see cref="Connect"/>.
/// </para>
/// </remarks>
public sealed class RasDialVpnConnector : IVpnConnector
{
    /// <summary>Windows gives VPN handshakes plenty of room; so do we.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(90);

    /// <summary>Querying and dropping a connection are local and immediate.</summary>
    public static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(20);

    private const string DisconnectSwitch = "/disconnect";
    private const string ExecutableName = "rasdial.exe";

    private readonly IProcessRunner _processRunner;
    private readonly string _executablePath;

    public RasDialVpnConnector(IProcessRunner processRunner, string? executablePath = null)
    {
        _processRunner = processRunner;
        _executablePath = executablePath ?? DefaultExecutablePath();
    }

    /// <summary>
    /// Resolved from <c>%SystemRoot%\System32</c> rather than looked up on PATH.
    /// A tool that authenticates against a client's network should not be able to
    /// be redirected by a stray <c>rasdial.exe</c> in the current directory.
    /// </summary>
    public static string DefaultExecutablePath()
    {
        var system32 = Path.Combine(Environment.SystemDirectory, ExecutableName);
        return File.Exists(system32) ? system32 : ExecutableName;
    }

    /// <inheritdoc />
    public bool IsConnected(VpnSettings settings)
    {
        var name = RequireConnectionName(settings);

        // rasdial with no arguments lists the active connections.
        return ListsConnection(Run([], QueryTimeout), name);
    }

    /// <inheritdoc />
    public IVpnSession Connect(VpnSettings settings)
    {
        var name = RequireConnectionName(settings);

        // Checked first so that an operator who is already connected - possibly
        // mid-call with the client - keeps that connection untouched, and so a
        // second run of the tool is not an error.
        if (IsConnected(settings))
        {
            return VpnSession.Adopted(name);
        }

        var result = Run(BuildConnectArguments(settings, name), ConnectTimeout);

        if (!result.Succeeded)
        {
            throw new VpnException(
                $"Could not connect to VPN '{name}'. {DescribeFailure(result.ExitCode)}" +
                FormatDetail(result));
        }

        return VpnSession.Opened(name, () => Disconnect(settings));
    }

    /// <inheritdoc />
    public void Disconnect(VpnSettings settings)
    {
        var name = RequireConnectionName(settings);

        // Asking rasdial to drop a connection that is already down produces an
        // error code that varies by Windows build. Checking first turns that into
        // a reliable no-op, which is what "disconnect" should mean when there is
        // nothing to disconnect.
        if (!IsConnected(settings))
        {
            return;
        }

        var result = Run([name, DisconnectSwitch], QueryTimeout);

        if (!result.Succeeded)
        {
            throw new VpnException(
                $"Could not disconnect VPN '{name}'. {DescribeFailure(result.ExitCode)}" +
                FormatDetail(result));
        }
    }

    // --- internals -------------------------------------------------------

    /// <summary>
    /// Builds the argument list, omitting credentials when the saved ones will do.
    /// </summary>
    /// <remarks>
    /// <b>Known trade-off.</b> When a password is supplied it appears in
    /// <c>rasdial</c>'s command line, which is readable by other processes running
    /// as the same user for as long as the process lives - a second or two. This
    /// is a property of <c>rasdial</c>, not of how it is called here: it has no
    /// mechanism for accepting a password any other way. Leaving the credentials
    /// saved in the Windows connection and omitting them here avoids the exposure
    /// completely, which is why that is the documented recommendation.
    /// </remarks>
    private static List<string> BuildConnectArguments(VpnSettings settings, string name)
    {
        var hasUsername = !string.IsNullOrWhiteSpace(settings.Username);
        var hasPassword = !string.IsNullOrWhiteSpace(settings.Password);

        if (hasUsername != hasPassword)
        {
            // rasdial prompts for whichever half is missing. Standard input is
            // closed, so it would fail anyway - just less clearly than this.
            throw new VpnException(
                $"VPN '{name}' has a username but no password, or the reverse. " +
                "Set both, or set neither to use the credentials saved in the " +
                "Windows VPN connection.");
        }

        return hasUsername
            ? [name, settings.Username!, settings.Password!]
            : [name];
    }

    private ProcessResult Run(IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new VpnException(
                "The Windows RAS connector needs rasdial.exe and only runs on Windows. " +
                "Set the client's vpn type to 'None' and connect by hand.");
        }

        try
        {
            return _processRunner.Run(_executablePath, arguments, timeout);
        }
        catch (ProcessExecutionException ex)
        {
            throw new VpnException(ex.Message, ex);
        }
    }

    /// <summary>
    /// Whether rasdial's listing mentions the connection.
    /// </summary>
    /// <remarks>
    /// A substring check, not a parse of the "Connected to X" line, because that
    /// wording is localised and this tool runs on machines that are not
    /// necessarily English. The connection name itself is the one token in that
    /// output guaranteed not to be translated. The cost is that a connection
    /// named something like "VPN" could match unrelated text; the fix, should it
    /// ever matter, is to name the entry distinctly.
    /// </remarks>
    private static bool ListsConnection(ProcessResult result, string connectionName) =>
        result.StandardOutput.Contains(connectionName, StringComparison.OrdinalIgnoreCase);

    private static string RequireConnectionName(VpnSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(settings.ConnectionName))
        {
            throw new VpnException(
                "This client's vpn type is 'WindowsRas' but no connectionName is set. " +
                "It must match the Windows VPN entry exactly - run " +
                "'Get-VpnConnection | Select-Object Name' to see the available names.");
        }

        return settings.ConnectionName;
    }

    /// <summary>
    /// Turns a RAS error code into something worth reading.
    /// </summary>
    /// <remarks>
    /// rasdial exits with the RAS error number, which is a genuinely useful
    /// signal - "691" distinguishes a wrong password from a blocked port without
    /// any guesswork. Left as a bare number it means nothing to the operator, so
    /// the codes that this tool can realistically provoke are translated into the
    /// action each one calls for.
    /// </remarks>
    private static string DescribeFailure(int exitCode) => exitCode switch
    {
        623 => "Windows has no VPN entry by that name (623). Check connectionName " +
               "against 'Get-VpnConnection | Select-Object Name'.",
        691 => "The username or password was rejected (691).",
        735 => "The server rejected the requested address (735).",
        741 or 742 => $"The two ends disagree on encryption settings ({exitCode}).",
        789 => "The security layer failed during negotiation (789) - usually a " +
               "pre-shared key or certificate problem on an L2TP connection.",
        800 => "The VPN tunnel could not be established (800). The server is " +
               "unreachable, or the VPN protocol is blocked on this network.",
        806 => "GRE (protocol 47) is being blocked (806) - common on hotel, " +
               "mobile and guest networks.",
        809 => "The network is blocking the VPN (809). UDP 500 and 4500 are " +
               "typically what a restrictive network drops.",
        812 => "The server refused the connection on policy grounds (812).",
        13801 => "The credentials were not accepted by IKE (13801).",
        _ => $"rasdial reported error {exitCode}."
    };

    /// <summary>
    /// Appends rasdial's own output. Safe to show: rasdial echoes the connection
    /// name and its error text, never the credentials it was given.
    /// </summary>
    private static string FormatDetail(ProcessResult result)
    {
        var detail = result.CombinedOutput;

        return string.IsNullOrWhiteSpace(detail)
            ? string.Empty
            : Environment.NewLine + detail;
    }
}
