using QadAutomation.Cli;
using QadAutomation.Core.Tests.Vpn;
using QadAutomation.Core.Vpn;

namespace QadAutomation.Core.Tests.Cli;

/// <summary>
/// The VPN commands driven through the real entry point.
/// </summary>
/// <remarks>
/// Every layer here is the production one - parser, composition root, command,
/// <c>RasDialVpnConnector</c> - except <c>rasdial.exe</c> itself. That is the
/// narrowest possible substitution, so what these tests prove about argument
/// handling, exit codes and messages holds for the real run too.
/// </remarks>
public sealed class VpnCommandTests : IDisposable
{
    private const string ConnectionName = "PilotVpn";

    private readonly string _folder;
    private readonly string _configPath;
    private readonly FakeRasDial _rasDial = new();

    public VpnCommandTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "qad-vpn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);

        _configPath = Path.Combine(_folder, "config.json");
        File.WriteAllText(_configPath,
            $$"""
            {
              "workingFolder": "{{_folder.Replace("\\", "\\\\")}}",
              "clients": [{
                "id": "pilot",
                "displayName": "Pilot Client",
                "vpn": { "type": "WindowsRas", "connectionName": "{{ConnectionName}}" },
                "defaults": {
                  "host": "qad.example", "username": "qad", "password": "pw",
                  "srcRemotePath": "/qad/src",
                  "compile": { "qrf": { "editor": { "editorCommand": "compile_editor us test" } } }
                },
                "environments": [{ "name": "TEST" }]
              }]
            }
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Status_reports_a_disconnected_vpn()
    {
        var (exitCode, output, _) = Run("vpn", "status", "pilot");

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Contains("is not connected", output);
    }

    [Fact]
    public void Connect_brings_the_vpn_up_and_leaves_it_up()
    {
        // The distinguishing behaviour of 'qad vpn connect': the process exits
        // and the operator still has a working VPN.
        var (exitCode, output, _) = Run("vpn", "connect", "pilot");

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Contains("Connected to", output);
        Assert.True(_rasDial.IsUp(ConnectionName));
    }

    [Fact]
    public void Connect_says_so_when_the_vpn_was_already_up()
    {
        _rasDial.SetAlreadyConnected(ConnectionName);

        var (exitCode, output, _) = Run("vpn", "connect", "pilot");

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Contains("was already connected", output);
    }

    [Fact]
    public void Disconnect_takes_it_down()
    {
        _rasDial.SetAlreadyConnected(ConnectionName);

        var (exitCode, output, _) = Run("vpn", "disconnect", "pilot");

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Contains("Disconnected from", output);
        Assert.False(_rasDial.IsUp(ConnectionName));
    }

    [Fact]
    public void A_refused_connection_exits_with_the_vpn_code_and_no_stack_trace()
    {
        _rasDial.ConnectFailureCode = 691;

        var (exitCode, _, error) = Run("vpn", "connect", "pilot");

        Assert.Equal(ExitCode.VpnError, exitCode);
        Assert.Contains("username or password was rejected", error);
        Assert.DoesNotContain("   at ", error);
    }

    [Fact]
    public void An_unknown_client_is_a_configuration_error_and_never_touches_the_network()
    {
        var (exitCode, _, error) = Run("vpn", "connect", "nosuchclient");

        Assert.Equal(ExitCode.ConfigurationError, exitCode);
        Assert.Contains("Available: pilot", error);
        Assert.Empty(_rasDial.Calls);
    }

    [Fact]
    public void An_unknown_vpn_action_is_a_usage_error()
    {
        var (exitCode, _, error) = Run("vpn", "reconnect", "pilot");

        Assert.Equal(ExitCode.UsageError, exitCode);
        Assert.Contains("Unknown vpn action", error);
    }

    [Fact]
    public void A_missing_client_argument_is_a_usage_error()
    {
        var (exitCode, _, error) = Run("vpn", "connect");

        Assert.Equal(ExitCode.UsageError, exitCode);
        Assert.Contains("Not enough arguments", error);
    }

    [Fact]
    public void An_unquoted_argument_containing_a_space_is_reported_not_ignored()
    {
        // 'qad ticket Ticket #9999555' arrives as two arguments. Acting on the
        // first would show the wrong ticket without saying anything.
        var (exitCode, _, error) = Run("ticket", "Ticket", "#9999555");

        Assert.Equal(ExitCode.UsageError, exitCode);
        Assert.Contains("quote it", error);
    }

    [Fact]
    public void Help_lists_the_vpn_commands()
    {
        var (_, output, _) = Run("help");

        Assert.Contains("qad vpn connect", output);
    }

    private (int ExitCode, string Output, string Error) Run(params string[] command)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = new CommandLineApplication(output, error, new VpnConnectorFactory(_rasDial))
            .Run([.. command, "--config", _configPath]);

        return (exitCode, output.ToString(), error.ToString());
    }
}
