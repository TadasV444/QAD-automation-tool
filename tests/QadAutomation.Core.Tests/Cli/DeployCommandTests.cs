using QadAutomation.Cli;
using QadAutomation.Core.Tests.Compile;
using QadAutomation.Core.Tests.Transfer;
using QadAutomation.Core.Tests.Vpn;
using QadAutomation.Core.Vpn;

namespace QadAutomation.Core.Tests.Cli;

/// <summary>
/// <c>qad deploy</c> - the whole manual workflow in one command.
/// </summary>
public sealed class DeployCommandTests : IDisposable
{
    private const string SrcPath = "/appl/qad/global/xrc";
    private const string QrfPath = "/appl/qad/desktop/test/reports";
    private const string VpnName = "PilotVpn";

    private readonly string _root;
    private readonly string _configPath;
    private readonly FakeRasDial _rasDial = new();
    private readonly FakeSftpServer _server = new FakeSftpServer().WithDirectory(SrcPath, QrfPath);
    private readonly FakeSshShell _shell = new();

    public DeployCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "qad-dply-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path.Combine(_root, "tasks", "Ticket #9999555", "QRF"));
        File.WriteAllText(Path.Combine(_root, "tasks", "Ticket #9999555", "QRF", "rep_b.p"), "QRF B");

        _shell.Server = _server;

        _configPath = Path.Combine(_root, "config.json");
        File.WriteAllText(_configPath,
            $$"""
            {
              "workingFolder": "{{Path.Combine(_root, "tasks").Replace("\\", "\\\\")}}",
              "clients": [{
                "id": "pilot",
                "displayName": "Pilot Client",
                "vpn": { "type": "WindowsRas", "connectionName": "{{VpnName}}" },
                "defaults": {
                  "host": "qad.example", "username": "mfg", "password": "hunter2",
                  "srcRemotePath": "{{SrcPath}}",
                  "qrfRemotePath": "{{QrfPath}}",
                  "compile": { "qrf": { "editorCommand": "{{QrfPath}}/compile_editor us test" } }
                },
                "environments": [ { "name": "TEST" }, { "name": "PROD" } ]
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
    public void The_whole_workflow_runs_in_one_vpn_session()
    {
        _shell.Produces.Add($"{QrfPath}/rep_b.r");

        var (exitCode, output, _) = Run("deploy", "pilot", "TEST", "9999555");

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Equal("QRF B", _server.Files[$"{QrfPath}/rep_b.p"]);
        Assert.Contains("1 created", output);
        Assert.Contains("1 compiled, 0 failed", output);

        // The point of one command rather than two: the VPN is dialled once.
        Assert.Single(_rasDial.ConnectCalls);
        Assert.False(_rasDial.IsUp(VpnName));
    }

    [Fact]
    public void Both_plans_are_shown_before_anything_connects()
    {
        // The reason deploy exists as its own command: the operator decides
        // once, seeing what will be uploaded AND what will be compiled. Run as
        // two commands, the compile is only described after the upload has
        // already happened.
        var (exitCode, output, _) = Run("deploy", "pilot", "TEST", "9999555", "--dry-run");

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Contains("1 file(s) to upload", output);
        Assert.Contains("1 program(s) to compile", output);
        Assert.Contains($"compile {QrfPath}/rep_b.p save into {QrfPath}.", output);

        Assert.Empty(_rasDial.ConnectCalls);
        Assert.Null(_server.ConnectedTo);
        Assert.Null(_shell.OpenedFor);
    }

    [Fact]
    public void A_failed_upload_stops_the_run_before_the_compile()
    {
        // Compiling after a partial upload would build some new programs and
        // some old ones, with nothing to say which.
        var server = new FakeSftpServer(); // no directories: the upload cannot land

        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = new CommandLineApplication(
                output, error, new VpnConnectorFactory(_rasDial), server, _shell)
            .Run(["deploy", "pilot", "TEST", "9999555", "--config", _configPath]);

        Assert.Equal(ExitCode.TransferError, exitCode);
        Assert.Null(_shell.OpenedFor);
        Assert.False(_rasDial.IsUp(VpnName));
    }

    [Fact]
    public void A_failed_compile_is_reported_even_though_the_upload_worked()
    {
        _server.WithFile($"{QrfPath}/rep_b.r", "STALE");

        var (exitCode, output, error) = Run("deploy", "pilot", "TEST", "9999555");

        Assert.Equal(ExitCode.CompileError, exitCode);

        // The upload half still happened and still says so.
        Assert.Contains("1 created", output);
        Assert.Contains("did not compile", error);
    }

    [Fact]
    public void Production_is_refused_without_an_explicit_yes()
    {
        var (exitCode, _, error) = Run("deploy", "pilot", "PROD", "9999555");

        Assert.Equal(ExitCode.UsageError, exitCode);
        Assert.Contains("PRODUCTION", error);
        Assert.Empty(_server.Files);
        Assert.Null(_shell.OpenedFor);
    }

    [Fact]
    public void A_src_program_is_uploaded_but_reported_as_not_compiled()
    {
        Directory.CreateDirectory(Path.Combine(_root, "tasks", "Ticket #9999555", "SRC"));
        File.WriteAllText(Path.Combine(_root, "tasks", "Ticket #9999555", "SRC", "prog_a.p"), "SRC A");

        _shell.Produces.Add($"{QrfPath}/rep_b.r");

        var (exitCode, output, _) = Run("deploy", "pilot", "TEST", "9999555");

        // Uploaded, so it is on the server ready to be compiled by hand.
        Assert.Equal("SRC A", _server.Files[$"{SrcPath}/prog_a.p"]);

        // But the run must not read as finished.
        Assert.NotEqual(ExitCode.Ok, exitCode);
        Assert.Contains("will NOT be compiled", output);
    }

    [Fact]
    public void The_password_never_appears_in_the_output()
    {
        _shell.Produces.Add($"{QrfPath}/rep_b.r");

        var (_, output, error) = Run("deploy", "pilot", "TEST", "9999555");

        Assert.DoesNotContain("hunter2", output + error);
    }

    private (int ExitCode, string Output, string Error) Run(params string[] command)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = new CommandLineApplication(
                output, error, new VpnConnectorFactory(_rasDial), _server, _shell)
            .Run([.. command, "--config", _configPath]);

        return (exitCode, output.ToString(), error.ToString());
    }
}
