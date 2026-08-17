using QadAutomation.Core.Configuration;
using QadAutomation.Core.Transfer;
using QadAutomation.Core.Vpn;

namespace QadAutomation.Cli.Commands;

/// <summary>
/// <c>qad check &lt;client&gt; &lt;environment&gt;</c> - prove the connection works,
/// without transferring anything.
/// </summary>
/// <remarks>
/// <para>
/// The same tactic that made the VPN step easy to trust, applied one layer up.
/// <c>qad vpn connect</c> proved the tunnel on its own; this proves the tunnel
/// <i>plus</i> SFTP login <i>plus</i> that the configured remote directories
/// actually exist - still without writing a single byte to a client's server.
/// </para>
/// <para>
/// Worth having as its own command rather than folding into <c>upload</c>,
/// because the questions differ. Upload asks "did my files arrive?"; this asks
/// "is the config right?", and it can be answered before there is any ticket to
/// deploy and re-answered any time something looks wrong. It is read-only, so
/// running it against production is safe.
/// </para>
/// </remarks>
public sealed class CheckCommand
{
    /// <summary>Enough entries to recognise a directory, few enough to read.</summary>
    private const int SampleSize = 5;

    private readonly IConfigurationLoader _loader;
    private readonly IVpnConnectorFactory _connectors;
    private readonly ISftpSessionFactory _sessions;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public CheckCommand(
        IConfigurationLoader loader,
        IVpnConnectorFactory connectors,
        ISftpSessionFactory sessions,
        TextWriter output,
        TextWriter error)
    {
        _loader = loader;
        _connectors = connectors;
        _sessions = sessions;
        _output = output;
        _error = error;
    }

    public int Execute(string clientId, string environmentName)
    {
        var client = _loader.Load().Configuration.RequireClient(clientId);
        var environment = client.RequireEnvironment(environmentName);

        _output.WriteLine($"Client      : {client.DisplayName} [{client.Id}]");
        _output.WriteLine($"Environment : {environment.Name}{(environment.IsProduction ? "   ** PRODUCTION **" : "")}");
        _output.WriteLine();

        var connector = _connectors.Create(client.Vpn);

        // Restores whatever it found, exactly as the upload path does.
        using var session = ConnectVpn(connector, client);

        var endpoint = environment.Ssh;
        _output.WriteLine($"2. SFTP {endpoint.Username}@{endpoint.Host}:{endpoint.Port}");

        using var sftp = _sessions.Connect(endpoint);

        _output.WriteLine("   connected.");
        _output.WriteLine($"   host key: {sftp.HostKeyFingerprint}");
        _output.WriteLine();

        _output.WriteLine("3. Remote directories");

        var problems =
            CheckPath(sftp, "SRC", environment.Paths.Src) +
            CheckPath(sftp, "QRF", environment.Paths.Qrf);

        _output.WriteLine();

        if (problems > 0)
        {
            _error.WriteLine(
                $"{problems} configured path(s) do not exist on the server. " +
                "An upload would refuse rather than create them - fix srcRemotePath / " +
                "qrfRemotePath, or check you are pointed at the right environment.");
            return ExitCode.TransferError;
        }

        _output.WriteLine("All good. This environment is ready to upload to.");
        return ExitCode.Ok;
    }

    private IVpnSession ConnectVpn(IVpnConnector connector, ClientProfile client)
    {
        _output.WriteLine($"1. VPN {DescribeVpn(client.Vpn)}");

        var session = connector.Connect(client.Vpn);

        _output.WriteLine(session.OpenedByTool
            ? "   connected (will disconnect when this command finishes)."
            : "   already connected - leaving it as it is.");

        _output.WriteLine();
        return session;
    }

    /// <summary>
    /// Reports whether one configured directory exists, and what is in it.
    /// </summary>
    /// <returns>1 if the path is configured but missing, otherwise 0.</returns>
    /// <remarks>
    /// A listing rather than a bare "exists" because the useful failure is not
    /// an absent directory - it is a directory that exists but is the wrong one.
    /// Seeing familiar program names is what confirms the path is right.
    /// </remarks>
    private int CheckPath(ISftpSession sftp, string label, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _output.WriteLine($"   {label}: (not configured)");
            return 0;
        }

        if (!sftp.Exists(path))
        {
            _output.WriteLine($"   {label}: MISSING  {path}");
            return 1;
        }

        _output.WriteLine($"   {label}: ok       {path}");

        // A permission problem shows up here rather than at upload time, which
        // is the whole point of checking early.
        IReadOnlyList<string> entries;
        try
        {
            entries = sftp.List(path);
        }
        catch (TransferException ex)
        {
            _output.WriteLine($"          could not list it: {ex.Message}");
            return 1;
        }

        _output.WriteLine($"          {entries.Count} entr{(entries.Count == 1 ? "y" : "ies")}" +
                          (entries.Count == 0 ? string.Empty : ", for example:"));

        foreach (var entry in entries.Take(SampleSize))
        {
            _output.WriteLine($"            {entry}");
        }

        return 0;
    }

    private static string DescribeVpn(VpnSettings vpn) => vpn.Type switch
    {
        VpnType.None => "(not managed by this tool)",
        VpnType.WindowsRas => $"'{vpn.ConnectionName}'",
        _ => vpn.Type.ToString()
    };
}
