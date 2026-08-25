using QadAutomation.Core.Configuration;

namespace QadAutomation.Core.Vpn;

/// <summary>
/// Checks a FortiClient tunnel is up. Does not dial it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this one only looks.</b> The FortiClient VPN edition installed at these
/// sites ships <c>fortisslvpnsys.exe</c> and <c>fortisslvpndaemon.exe</c> and no
/// connect command - the <c>FortiSSLVPNclient.exe</c> older versions had is gone.
/// The tunnels are SSL-VPN, which the Windows client cannot dial either, so the
/// <c>rasdial</c> path cannot be reused. That leaves driving the application's
/// window, which was rejected in Step 2 and is rejected again here: it breaks on
/// any redesign, it breaks silently, and it breaks in the direction of clicking
/// something unintended on a client's network.
/// </para>
/// <para>
/// So the tool does the part it can do reliably. One check removes the failure
/// that actually wastes time - starting an upload with no tunnel and getting a
/// connection timeout half a minute later, from a message that names the wrong
/// problem.
/// </para>
/// <para>
/// The operator still clicks Connect once. That is worse than the RAS clients,
/// and being honest about which part is automated matters more here than
/// elsewhere: a VPN the tool claims to manage but silently does not is how
/// somebody comes to believe they are on a client's network when they are not.
/// </para>
/// </remarks>
public sealed class FortiClientVpnConnector : IVpnConnector
{
    /// <summary>
    /// What a Fortinet tunnel adapter tends to call itself.
    /// </summary>
    /// <remarks>
    /// Windows names the adapter from the driver, not the tunnel, so these match
    /// the common installs without any configuration. A site whose adapter reads
    /// differently sets <c>adapterName</c>, and then none of these are consulted.
    /// </remarks>
    private static readonly string[] DefaultMatches = ["fortinet", "fortissl", "forticlient"];

    /// <summary>Enough context to fix a wrong match without drowning the message.</summary>
    private const int AdaptersToList = 12;

    private readonly INetworkInterfaces _interfaces;

    public FortiClientVpnConnector(INetworkInterfaces interfaces) => _interfaces = interfaces;

    /// <inheritdoc />
    public bool IsConnected(VpnSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var active = _interfaces.Active();

        var matches = settings.AdapterName is { } configured ? [configured] : DefaultMatches;

        return active.Any(adapter =>
            matches.Any(match => adapter.Contains(match, StringComparison.OrdinalIgnoreCase)));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Never opens anything, so the session is always an adopted one and dispose
    /// never disconnects. Taking down a tunnel this tool did not raise would be
    /// wrong even if it could.
    /// </remarks>
    public IVpnSession Connect(VpnSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var name = settings.ConnectionName ?? "the FortiClient tunnel";

        if (IsConnected(settings))
        {
            return VpnSession.Adopted(name);
        }

        // The adapters are listed because the other explanation for landing here
        // is a tunnel that IS up under a name we do not recognise - and without
        // seeing the list there is no way to tell the two apart, or to know what
        // to put in 'adapterName'.
        var active = _interfaces.Active().Take(AdaptersToList);

        throw new VpnException(
            $"'{name}' does not appear to be connected. FortiClient has no command line " +
            $"this tool can use, so connect '{name}' in FortiClient and run this again." +
            Environment.NewLine +
            Environment.NewLine +
            "If it IS connected, its network adapter is not being recognised. These are up:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, active.Select(adapter => "  - " + adapter)) +
            Environment.NewLine +
            $"Set 'vpn.adapterName' for this client to distinctive text from the right one.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Refuses rather than pretending. Reporting a disconnect for a tunnel still
    /// carrying traffic is the failure worth avoiding here.
    /// </remarks>
    public void Disconnect(VpnSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        throw new VpnException(
            "FortiClient cannot be disconnected by this tool. Close the tunnel in " +
            "FortiClient itself.");
    }
}
