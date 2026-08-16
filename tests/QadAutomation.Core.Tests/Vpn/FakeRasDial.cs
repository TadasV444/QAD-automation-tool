using QadAutomation.Core.Processes;

namespace QadAutomation.Core.Tests.Vpn;

/// <summary>
/// A stand-in for <c>rasdial.exe</c> that remembers which connections are up.
/// </summary>
/// <remarks>
/// <para>
/// Not a mock. A mock would assert that some call was made; this reproduces
/// rasdial's actual behaviour - listing connections when given no arguments,
/// connecting when given a name, and reporting the RAS error code as its exit
/// code - so the tests exercise the connector's real logic against a realistic
/// counterparty rather than against an expectation someone wrote down.
/// </para>
/// <para>
/// This is the payoff for <c>IProcessRunner</c> being an interface. Getting a
/// real rasdial to return 691 on demand means deliberately breaking a password
/// against a client's VPN server; here it is a property assignment.
/// </para>
/// </remarks>
internal sealed class FakeRasDial : IProcessRunner
{
    private readonly HashSet<string> _connected = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every argument list passed, in order.</summary>
    public List<string[]> Calls { get; } = [];

    /// <summary>Non-zero to make the next connect attempt fail with that RAS code.</summary>
    public int ConnectFailureCode { get; set; }

    /// <summary>Pretend the operator already brought this connection up by hand.</summary>
    public void SetAlreadyConnected(string name) => _connected.Add(name);

    public bool IsUp(string name) => _connected.Contains(name);

    /// <summary>Argument lists that were an actual connect attempt.</summary>
    public IEnumerable<string[]> ConnectCalls =>
        Calls.Where(c => c.Length > 0 && !c.Contains("/disconnect"));

    public IEnumerable<string[]> DisconnectCalls =>
        Calls.Where(c => c.Contains("/disconnect"));

    public ProcessResult Run(string executablePath, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        Calls.Add([.. arguments]);

        // No arguments: list the active connections.
        if (arguments.Count == 0)
        {
            var listing = _connected.Count == 0
                ? "No connections"
                : string.Join(Environment.NewLine, _connected.Select(name => $"Connected to {name}"));

            return new ProcessResult(0, listing + Environment.NewLine + "Command completed successfully.", "");
        }

        var connectionName = arguments[0];

        if (arguments.Contains("/disconnect"))
        {
            _connected.Remove(connectionName);
            return new ProcessResult(0, "Command completed successfully.", "");
        }

        if (ConnectFailureCode != 0)
        {
            // rasdial reports the code and its text, never the credentials.
            return new ProcessResult(
                ConnectFailureCode,
                "",
                $"Remote Access error {ConnectFailureCode}");
        }

        _connected.Add(connectionName);
        return new ProcessResult(0, $"Connecting to {connectionName}...{Environment.NewLine}Successfully connected.", "");
    }
}

/// <summary>
/// An <see cref="IProcessRunner"/> that cannot start the program at all.
/// </summary>
internal sealed class UnstartableProcessRunner : IProcessRunner
{
    public ProcessResult Run(string executablePath, IReadOnlyList<string> arguments, TimeSpan timeout) =>
        throw new ProcessExecutionException("Could not run 'rasdial.exe': the system cannot find the file.");
}
