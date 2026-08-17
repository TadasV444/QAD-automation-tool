using QadAutomation.Cli;
using QadAutomation.Core.Tests.Transfer;
using QadAutomation.Core.Tests.Vpn;
using QadAutomation.Core.Vpn;

namespace QadAutomation.Core.Tests.Cli;

public sealed class CheckCommandTests : IDisposable
{
    private const string SrcPath = "/appl/global/xrc";
    private const string QrfPath = "/appl/desktop/test/reports";
    private const string VpnName = "PilotVpn";

    private readonly string _root;
    private readonly string _configPath;
    private readonly FakeRasDial _rasDial = new();
    private readonly FakeSftpServer _server = new();

    public CheckCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "qad-chk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _configPath = Path.Combine(_root, "config.json");
        File.WriteAllText(_configPath,
            $$"""
            {
              "workingFolder": "{{_root.Replace("\\", "\\\\")}}",
              "clients": [{
                "id": "pilot",
                "displayName": "Pilot Client",
                "vpn": { "type": "WindowsRas", "connectionName": "{{VpnName}}" },
                "defaults": {
                  "host": "qad.example", "username": "mfg", "password": "hunter2",
                  "qrfRemotePath": "{{QrfPath}}",
                  "compile": { "strategy": "InteractiveMenu", "commands": [] }
                },
                "environments": [
                  { "name": "TEST", "srcRemotePath": "{{SrcPath}}" },
                  { "name": "PROD" }
                ]
              }]
            }
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void A_healthy_environment_reports_all_good()
    {
        _server.WithDirectory(SrcPath, QrfPath)
               .WithFile($"{SrcPath}/existing.p", "x");

        var (exitCode, output, _) = Run("check", "pilot", "TEST");

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Contains("SRC: ok", output);
        Assert.Contains("QRF: ok", output);
        Assert.Contains("All good", output);
    }

    [Fact]
    public void It_shows_what_is_in_each_directory()
    {
        // A directory that exists but is the WRONG one is the failure that
        // matters; seeing familiar program names is what rules it out.
        _server.WithDirectory(SrcPath, QrfPath)
               .WithFile($"{SrcPath}/prog_one.p", "x")
               .WithFile($"{SrcPath}/prog_two.p", "x");

        var (_, output, _) = Run("check", "pilot", "TEST");

        Assert.Contains("2 entries", output);
        Assert.Contains("prog_one.p", output);
    }

    [Fact]
    public void A_missing_remote_directory_is_reported_and_fails()
    {
        _server.WithDirectory(SrcPath); // no QRF

        var (exitCode, output, error) = Run("check", "pilot", "TEST");

        Assert.Equal(ExitCode.TransferError, exitCode);
        Assert.Contains($"QRF: MISSING  {QrfPath}", output);
        Assert.Contains("do not exist on the server", error);
    }

    [Fact]
    public void A_path_that_is_not_configured_is_reported_but_is_not_a_failure()
    {
        // PROD has no srcRemotePath on purpose - that is a decision, not a fault,
        // so it is reported without failing the check.
        //
        // Note the fixture puts srcRemotePath on TEST rather than in defaults.
        // Writing "srcRemotePath": null on PROD would NOT clear an inherited
        // value - see Configuration_null_does_not_clear_an_inherited_value.
        _server.WithDirectory(QrfPath);

        var (exitCode, output, _) = Run("check", "pilot", "PROD");

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Contains("SRC: (not configured)", output);
    }

    [Fact]
    public void The_host_key_fingerprint_is_shown()
    {
        _server.WithDirectory(SrcPath, QrfPath);

        var (_, output, _) = Run("check", "pilot", "TEST");

        Assert.Contains("host key: ssh-ed25519 SHA256:", output);
    }

    [Fact]
    public void The_vpn_is_connected_and_then_restored()
    {
        _server.WithDirectory(SrcPath, QrfPath);

        Run("check", "pilot", "TEST");

        Assert.Single(_rasDial.ConnectCalls);
        Assert.False(_rasDial.IsUp(VpnName));
    }

    [Fact]
    public void A_vpn_the_operator_already_had_open_is_left_alone()
    {
        _rasDial.SetAlreadyConnected(VpnName);
        _server.WithDirectory(SrcPath, QrfPath);

        var (_, output, _) = Run("check", "pilot", "TEST");

        Assert.Contains("already connected", output);
        Assert.True(_rasDial.IsUp(VpnName));
    }

    [Fact]
    public void A_refused_login_exits_with_the_transfer_code_and_no_stack_trace()
    {
        _server.ConnectFailure = new Core.Transfer.TransferException(
            "'mfg' was refused by qad.example. Check the username and password in .env.");

        var (exitCode, _, error) = Run("check", "pilot", "TEST");

        Assert.Equal(ExitCode.TransferError, exitCode);
        Assert.Contains("Check the username and password", error);
        Assert.DoesNotContain("   at ", error);
    }

    [Fact]
    public void Nothing_is_ever_written_to_the_server()
    {
        _server.WithDirectory(SrcPath, QrfPath).WithFile($"{SrcPath}/a.p", "original");

        Run("check", "pilot", "TEST");

        Assert.Equal("original", _server.Files[$"{SrcPath}/a.p"]);
        Assert.Single(_server.Files);
    }

    [Fact]
    public void The_password_never_appears_in_the_output()
    {
        _server.WithDirectory(SrcPath, QrfPath);

        var (_, output, error) = Run("check", "pilot", "TEST");

        Assert.DoesNotContain("hunter2", output + error);
    }

    private (int ExitCode, string Output, string Error) Run(params string[] command)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = new CommandLineApplication(
                output, error, new VpnConnectorFactory(_rasDial), _server)
            .Run([.. command, "--config", _configPath]);

        return (exitCode, output.ToString(), error.ToString());
    }
}
