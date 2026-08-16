using QadAutomation.Core.Configuration;
using QadAutomation.Core.Vpn;

namespace QadAutomation.Cli.Commands;

/// <summary>
/// <c>qad vpn status|connect|disconnect &lt;client&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// The first command in the tool that changes anything outside the process, so
/// it is deliberately the smallest useful one. Being able to run
/// <c>qad vpn connect pilot</c> on its own - and watch the tray icon
/// change - proves the connection name, the credentials and the RAS plumbing
/// independently of SFTP and compiling. When the full pipeline later fails, that
/// is one fewer layer to suspect.
/// </para>
/// <para>
/// <c>connect</c> leaves the VPN up on purpose. Automatic teardown belongs to the
/// end-to-end command, where the tool knows the work is finished; here the
/// operator is the one who knows.
/// </para>
/// </remarks>
public sealed class VpnCommand
{
    private readonly IConfigurationLoader _loader;
    private readonly IVpnConnectorFactory _connectors;
    private readonly TextWriter _output;

    public VpnCommand(IConfigurationLoader loader, IVpnConnectorFactory connectors, TextWriter output)
    {
        _loader = loader;
        _connectors = connectors;
        _output = output;
    }

    public int Status(string clientId)
    {
        var (client, connector) = Resolve(clientId);

        _output.WriteLine(connector.IsConnected(client.Vpn)
            ? $"{Describe(client)} is connected."
            : $"{Describe(client)} is not connected.");

        return ExitCode.Ok;
    }

    public int Connect(string clientId)
    {
        var (client, connector) = Resolve(clientId);

        using var session = connector.Connect(client.Vpn);

        // The whole point of this command: hand the connection back to the
        // operator rather than tearing it down as the process exits.
        session.KeepOpen();

        _output.WriteLine(session.OpenedByTool
            ? $"Connected to {Describe(client)}."
            : $"{Describe(client)} was already connected - left as it was.");

        return ExitCode.Ok;
    }

    public int Disconnect(string clientId)
    {
        var (client, connector) = Resolve(clientId);

        // Reported before the call, because afterwards there is no way to tell
        // "we disconnected it" from "it was already down".
        var wasConnected = connector.IsConnected(client.Vpn);

        connector.Disconnect(client.Vpn);

        _output.WriteLine(wasConnected
            ? $"Disconnected from {Describe(client)}."
            : $"{Describe(client)} was not connected.");

        return ExitCode.Ok;
    }

    private (ClientProfile Client, IVpnConnector Connector) Resolve(string clientId)
    {
        // Loading the configuration first means an unknown client id or a broken
        // config fails before anything touches the network.
        var client = _loader.Load().Configuration.RequireClient(clientId);

        return (client, _connectors.Create(client.Vpn));
    }

    private static string Describe(ClientProfile client) =>
        client.Vpn.ConnectionName is { Length: > 0 } name
            ? $"VPN '{name}' ({client.DisplayName})"
            : $"the VPN for {client.DisplayName}";
}
