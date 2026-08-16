using QadAutomation.Core.Configuration;
using QadAutomation.Core.Processes;

namespace QadAutomation.Core.Vpn;

/// <inheritdoc cref="IVpnConnectorFactory" />
public sealed class VpnConnectorFactory : IVpnConnectorFactory
{
    private readonly IProcessRunner _processRunner;

    public VpnConnectorFactory(IProcessRunner processRunner) => _processRunner = processRunner;

    /// <inheritdoc />
    public IVpnConnector Create(VpnSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.Type switch
        {
            VpnType.None => new NullVpnConnector(),
            VpnType.WindowsRas => new RasDialVpnConnector(_processRunner),

            // Configurable but not yet automated, and the message says so plainly.
            // FortiClient exposes no supported command-line equivalent of
            // rasdial, so automating it means driving its window - the one
            // genuinely fragile technique in an otherwise protocol-level design,
            // and not something to build before the RAS path has proved itself.
            VpnType.FortiClient => throw new VpnException(
                "FortiClient cannot be connected by this tool yet. Connect it by hand " +
                "first, and set the client's vpn type to 'None' so the tool stops asking."),

            _ => throw new VpnException($"No connector exists for VPN type '{settings.Type}'.")
        };
    }
}
