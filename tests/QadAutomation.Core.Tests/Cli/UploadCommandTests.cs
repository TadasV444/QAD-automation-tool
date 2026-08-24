using QadAutomation.Cli;
using QadAutomation.Core.Tests.Transfer;
using QadAutomation.Core.Tests.Vpn;
using QadAutomation.Core.Vpn;

namespace QadAutomation.Core.Tests.Cli;

/// <summary>
/// <c>qad upload</c> driven through the real entry point.
/// </summary>
/// <remarks>
/// Real parser, real composition root, real command, real plan, real uploader.
/// Only <c>rasdial</c> and the SFTP server are substituted - the two things that
/// cannot exist on a build machine.
/// </remarks>
public sealed class UploadCommandTests : IDisposable
{
    private const string SrcPath = "/appl/qad/global/xrc";
    private const string QrfPath = "/appl/qad/desktop/test/reports";
    private const string VpnName = "PilotVpn";

    private readonly string _root;
    private readonly string _configPath;
    private readonly FakeRasDial _rasDial = new();
    private readonly FakeSftpServer _server = new FakeSftpServer().WithDirectory(SrcPath, QrfPath);

    public UploadCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "qad-updl-" + Guid.NewGuid().ToString("N"));

        // A realistic ticket folder: QAD Tasks / Ticket #9999555 / SRC + QRF.
        Directory.CreateDirectory(Path.Combine(_root, "tasks", "Ticket #9999555", "SRC"));
        Directory.CreateDirectory(Path.Combine(_root, "tasks", "Ticket #9999555", "QRF"));
        File.WriteAllText(Path.Combine(_root, "tasks", "Ticket #9999555", "SRC", "prog_a.p"), "SRC A");
        File.WriteAllText(Path.Combine(_root, "tasks", "Ticket #9999555", "QRF", "rep_b.p"), "QRF B");

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
                  "compile": { "qrf": { "editor": { "editorCommand": "compile_editor us test" } } }
                },
                "environments": [
                  { "name": "TEST" },
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
    public void A_dry_run_prints_the_plan_and_touches_nothing()
    {
        var (exitCode, output, _) = Run("upload", "pilot", "TEST", "9999555", "--dry-run");

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Contains($"{SrcPath}/", output);
        Assert.Contains($"{QrfPath}/", output);
        Assert.Contains("[SRC] prog_a.p", output);
        Assert.Contains("[QRF] rep_b.p", output);
        Assert.Contains("Dry run", output);

        // Nothing connected: not the VPN, not the server.
        Assert.Empty(_rasDial.ConnectCalls);
        Assert.Null(_server.ConnectedTo);
    }

    [Fact]
    public void A_real_run_uploads_each_kind_to_its_own_directory()
    {
        var (exitCode, output, _) = Run("upload", "pilot", "TEST", "9999555");

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Equal("SRC A", _server.Files[$"{SrcPath}/prog_a.p"]);
        Assert.Equal("QRF B", _server.Files[$"{QrfPath}/rep_b.p"]);
        Assert.Contains("2 created", output);
    }

    [Fact]
    public void The_vpn_is_brought_up_for_the_upload_and_taken_back_down()
    {
        Run("upload", "pilot", "TEST", "9999555");

        Assert.Single(_rasDial.ConnectCalls);

        // Restored to how it was found - the tool opened it, so the tool closes it.
        Assert.False(_rasDial.IsUp(VpnName));
    }

    [Fact]
    public void A_vpn_the_operator_already_had_open_is_left_open()
    {
        _rasDial.SetAlreadyConnected(VpnName);

        Run("upload", "pilot", "TEST", "9999555");

        Assert.True(_rasDial.IsUp(VpnName));
        Assert.Empty(_rasDial.DisconnectCalls);
    }

    [Fact]
    public void The_vpn_is_still_taken_down_when_the_upload_fails()
    {
        // The reason teardown is tied to IDisposable rather than written out on
        // each path: the failure paths are the ones that get forgotten.
        _server.ConnectFailure = new Core.Transfer.TransferException("refused");

        var (exitCode, _, _) = Run("upload", "pilot", "TEST", "9999555");

        Assert.Equal(ExitCode.TransferError, exitCode);
        Assert.False(_rasDial.IsUp(VpnName));
    }

    [Fact]
    public void Production_is_refused_without_an_explicit_yes()
    {
        var (exitCode, _, error) = Run("upload", "pilot", "PROD", "9999555");

        Assert.Equal(ExitCode.UsageError, exitCode);
        Assert.Contains("PRODUCTION", error);
        Assert.Contains("--yes", error);
        Assert.Empty(_server.Files);
    }

    [Fact]
    public void Production_proceeds_when_confirmed()
    {
        var (exitCode, output, _) = Run("upload", "pilot", "PROD", "9999555", "--yes");

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Contains("** PRODUCTION **", output);
        Assert.Equal("SRC A", _server.Files[$"{SrcPath}/prog_a.p"]);
    }

    [Fact]
    public void A_production_dry_run_needs_no_confirmation()
    {
        // Looking is always safe, and needing --yes to look would train the
        // operator to type --yes reflexively.
        var (exitCode, _, _) = Run("upload", "pilot", "PROD", "9999555", "--dry-run");

        Assert.Equal(ExitCode.Ok, exitCode);
    }

    [Fact]
    public void An_existing_remote_file_is_backed_up_locally_and_the_undo_is_printed()
    {
        _server.WithFile($"{SrcPath}/prog_a.p", "PREVIOUS");

        var (_, output, _) = Run("upload", "pilot", "TEST", "9999555");

        Assert.Contains("1 replaced", output);
        Assert.Contains("Previous versions saved to", output);
        Assert.Contains("copy /Y", output);

        // The undo needs both halves: restore the file, then re-upload it.
        Assert.Contains("qad upload pilot TEST", output);

        var backup = Assert.Single(Directory.GetFiles(BackupRoot, "prog_a.p", SearchOption.AllDirectories));
        Assert.Equal("PREVIOUS", File.ReadAllText(backup));

        // And the server keeps no copy of its own.
        Assert.DoesNotContain(_server.Files.Values, v => v == "PREVIOUS");
    }

    [Fact]
    public void No_backup_is_taken_when_asked_not_to()
    {
        _server.WithFile($"{SrcPath}/prog_a.p", "PREVIOUS");

        var (_, output, _) = Run("upload", "pilot", "TEST", "9999555", "--no-backup");

        Assert.DoesNotContain("To undo", output);
        Assert.False(Directory.Exists(BackupRoot));
        Assert.DoesNotContain(_server.Files.Values, v => v == "PREVIOUS");
    }

    [Fact]
    public void A_missing_remote_directory_is_a_transfer_error_and_uploads_nothing()
    {
        var server = new FakeSftpServer().WithDirectory(SrcPath); // QRF missing

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = new CommandLineApplication(output, error, new VpnConnectorFactory(_rasDial), server)
            .Run(["upload", "pilot", "TEST", "9999555", "--config", _configPath]);

        Assert.Equal(ExitCode.TransferError, exitCode);
        Assert.Contains("Nothing was uploaded", error.ToString());
        Assert.Empty(server.Files);
    }

    [Fact]
    public void An_unknown_environment_fails_before_the_network()
    {
        var (exitCode, _, error) = Run("upload", "pilot", "STAGING", "9999555");

        Assert.Equal(ExitCode.ConfigurationError, exitCode);
        Assert.Empty(_rasDial.Calls);
        Assert.Null(_server.ConnectedTo);
    }

    [Fact]
    public void An_unknown_ticket_is_a_ticket_error()
    {
        var (exitCode, _, _) = Run("upload", "pilot", "TEST", "1234567");

        Assert.Equal(ExitCode.TicketError, exitCode);
        Assert.Null(_server.ConnectedTo);
    }

    [Fact]
    public void Too_few_arguments_is_a_usage_error()
    {
        var (exitCode, _, error) = Run("upload", "pilot", "TEST");

        Assert.Equal(ExitCode.UsageError, exitCode);
        Assert.Contains("qad upload", error);
    }

    [Fact]
    public void A_mistyped_flag_is_rejected_rather_than_ignored()
    {
        // The dangerous one: --dryrun silently ignored means an operator who
        // believes they ran a dry run has in fact uploaded to a live server.
        var (exitCode, _, error) = Run("upload", "pilot", "TEST", "9999555", "--dryrun");

        Assert.Equal(ExitCode.UsageError, exitCode);
        Assert.Contains("Unknown option", error);
        Assert.Empty(_server.Files);
    }

    [Fact]
    public void The_password_never_appears_in_the_output()
    {
        var (_, output, error) = Run("upload", "pilot", "TEST", "9999555");

        Assert.DoesNotContain("hunter2", output + error);
    }

    /// <summary>
    /// The ticket's backup folder. Its per-run sub-folder is named with the real
    /// clock - this command builds its own uploader - so tests search under here
    /// rather than pinning a timestamp.
    /// </summary>
    private string BackupRoot => Path.Combine(_root, "tasks", "Ticket #9999555", "_backup");

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
