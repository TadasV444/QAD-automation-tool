using QadAutomation.Core.Configuration;
using QadAutomation.Core.Processes;

namespace QadAutomation.Core.Vpn;

/// <inheritdoc cref="IVpnConnectorFactory" />
public sealed class VpnConnectorFactory : IVpnConnectorFactory
{
    private readonly IProcessRunner _processRunner;
    private readonly INetworkInterfaces _interfaces;

    /// <param name="processRunner">Runs <c>rasdial</c> for the Windows RAS type.</param>
    /// <param name="interfaces">
    /// Overridable so the FortiClient path can be exercised without a tunnel.
    /// Defaults to the real thing, so existing callers are unaffected.
    /// </param>
    public VpnConnectorFactory(IProcessRunner processRunner, INetworkInterfaces? interfaces = null)
    {
        _processRunner = processRunner;
        _interfaces = interfaces ?? new NetworkInterfaces();
    }

    /// <inheritdoc />
    public IVpnConnector Create(VpnSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.Type switch
        {
            VpnType.None => new NullVpnConnector(),
            VpnType.WindowsRas => new RasDialVpnConnector(_processRunner),

            // Verifies rather than dials - see FortiClientVpnConnector for why
            // that is the honest ceiling for this client.
            VpnType.FortiClient => new FortiClientVpnConnector(_interfaces),

            _ => throw new VpnException($"No connector exists for VPN type '{settings.Type}'.")
        };
    }
}
