using QadAutomation.Cli;
using QadAutomation.Core.Tests.Compile;
using QadAutomation.Core.Tests.Transfer;
using QadAutomation.Core.Tests.Vpn;
using QadAutomation.Core.Vpn;

namespace QadAutomation.Core.Tests.Cli;

/// <summary>
/// <c>qad compile</c> driven through the real entry point.
/// </summary>
/// <remarks>
/// Real parser, real composition root, real plan, real compiler. Only
/// <c>rasdial</c>, the SFTP server and the Progress editor are substituted.
/// </remarks>
public sealed class CompileCommandTests : IDisposable
{
    private const string SrcPath = "/appl/qad/global/xrc";
    private const string QrfPath = "/appl/qad/desktop/test/reports";
    private const string VpnName = "PilotVpn";

    private readonly string _root;
    private readonly string _configPath;
    private readonly FakeRasDial _rasDial = new();
    private readonly FakeSftpServer _server = new FakeSftpServer().WithDirectory(SrcPath, QrfPath);
    private readonly FakeSshShell _shell = new();

    public CompileCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "qad-cmpl-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path.Combine(_root, "tasks", "Ticket #9999555", "SRC"));
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
    public void A_dry_run_prints_the_statement_and_connects_to_nothing()
    {
        var (exitCode, output, _) = Run("compile", "pilot", "TEST", "9999555", "--dry-run");

        Assert.Equal(ExitCode.Ok, exitCode);

        // The statement is shown in full because it is what will be typed. A
        // wrong path here is the difference between compiling and compiling
        // the wrong thing.
        Assert.Contains($"compile {QrfPath}/rep_b.p save into {QrfPath}.", output);
        Assert.Contains($"{QrfPath}/rep_b.r", output);
        Assert.Contains("Dry run", output);

        Assert.Empty(_rasDial.ConnectCalls);
        Assert.Null(_shell.OpenedFor);
        Assert.Null(_server.ConnectedTo);
    }

    [Fact]
    public void A_real_run_types_the_statement_and_reports_the_result()
    {
        Uploaded("rep_b.p");
        _shell.Produces.Add($"{QrfPath}/rep_b.r");

        var (exitCode, output, _) = Run("compile", "pilot", "TEST", "9999555");

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Contains("1 compiled, 0 failed", output);
        Assert.Contains($"compile {QrfPath}/rep_b.p save into {QrfPath}.", _shell.Typed);
    }

    [Fact]
    public void A_failed_compile_exits_with_its_own_code_and_shows_the_screen()
    {
        Uploaded("rep_b.p");
        _server.WithFile($"{QrfPath}/rep_b.r", "STALE");
        _shell.Screen = "** Unable to understand after -- \"prin\". (247)";

        var (exitCode, output, error) = Run("compile", "pilot", "TEST", "9999555");

        Assert.Equal(ExitCode.CompileError, exitCode);
        Assert.Contains("0 compiled, 1 failed", output);
        Assert.Contains("did not compile", error);
        Assert.Contains("Unable to understand", error);
    }

    [Fact]
    public void The_progress_error_is_pulled_out_of_the_terminal_noise()
    {
        // Captured from a real failed compile, with the paths made generic. The
        // editor positions its cursor instead of writing newlines, so without
        // this the useful two lines arrive buried in escape sequences.
        Uploaded("rep_b.p");
        _server.WithFile($"{QrfPath}/rep_b.r", "STALE");
        _shell.Screen = Screen(
            "<ESC>[50;1HCompiling procedure...<ESC>[50;199H<ESC>[H<ESC>[J" +
            "<ESC>[20;60Hlqqqqqqqqqqqqq Error qqqqqqqqqqqqqk" +
            "<ESC>[21;60Hx<ESC>[21;76H** Unable to understand after -- \"testing\". (247)<ESC>[21;140Hx" +
            "<ESC>[23;60Hx /appl/qad/reports/rep_b.p  x" +
            "<ESC>[24;83HCould not understand line 15. (198)<ESC>[24;140Hx" +
            "<ESC>[26;60Hx qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq x" +
            "<ESC>[27;98H<<ESC>[4mO<ESC>[mK><ESC>[50;199H");

        var (_, _, error) = Run("compile", "pilot", "TEST", "9999555");

        Assert.Contains("Unable to understand after -- \"testing\". (247)", error);
        Assert.Contains("Could not understand line 15. (198)", error);

        // The noise that made the first real run unreadable.
        Assert.DoesNotContain("[50;199H", error);
        Assert.DoesNotContain("[21;76H", error);
        Assert.DoesNotContain("qqqq", error);
    }

    [Fact]
    public void A_src_program_is_reported_as_not_compiled_and_the_run_is_not_clean()
    {
        // A ticket whose SRC half was silently left unbuilt, reported as
        // success, is exactly what this tool exists to prevent.
        File.WriteAllText(Path.Combine(_root, "tasks", "Ticket #9999555", "SRC", "prog_a.p"), "SRC A");

        Uploaded("rep_b.p");
        _shell.Produces.Add($"{QrfPath}/rep_b.r");

        var (exitCode, output, _) = Run("compile", "pilot", "TEST", "9999555");

        Assert.NotEqual(ExitCode.Ok, exitCode);
        Assert.Contains("will NOT be compiled", output);
        Assert.Contains("prog_a.p", output);
    }

    [Fact]
    public void Production_is_refused_without_an_explicit_yes()
    {
        Uploaded("rep_b.p");

        var (exitCode, _, error) = Run("compile", "pilot", "PROD", "9999555");

        Assert.Equal(ExitCode.UsageError, exitCode);
        Assert.Contains("PRODUCTION", error);
        Assert.Null(_shell.OpenedFor);
    }

    [Fact]
    public void The_vpn_is_brought_up_and_taken_back_down()
    {
        Uploaded("rep_b.p");
        _shell.Produces.Add($"{QrfPath}/rep_b.r");

        Run("compile", "pilot", "TEST", "9999555");

        Assert.Single(_rasDial.ConnectCalls);
        Assert.False(_rasDial.IsUp(VpnName));
    }

    [Fact]
    public void Compiling_before_uploading_is_refused_with_the_reason()
    {
        var (exitCode, _, error) = Run("compile", "pilot", "TEST", "9999555");

        Assert.Equal(ExitCode.TransferError, exitCode);
        Assert.Contains("qad upload", error);

        // The VPN still goes back down on the failure path.
        Assert.False(_rasDial.IsUp(VpnName));
    }

    [Fact]
    public void A_mistyped_flag_is_rejected_rather_than_ignored()
    {
        var (exitCode, _, error) = Run("compile", "pilot", "TEST", "9999555", "--dryrun");

        Assert.Equal(ExitCode.UsageError, exitCode);
        Assert.Contains("Unknown option", error);
        Assert.Null(_shell.OpenedFor);
    }

    [Fact]
    public void The_password_never_appears_in_the_output()
    {
        Uploaded("rep_b.p");
        _shell.Produces.Add($"{QrfPath}/rep_b.r");

        var (_, output, error) = Run("compile", "pilot", "TEST", "9999555");

        Assert.DoesNotContain("hunter2", output + error);
    }

    /// <summary>
    /// Builds a terminal capture, with <c>&lt;ESC&gt;</c> standing in for the
    /// escape byte - a raw one in source is invisible in every editor and diff.
    /// </summary>
    private static string Screen(string withMarkers) =>
        withMarkers.Replace("<ESC>", ((char)27).ToString(), StringComparison.Ordinal);

    private void Uploaded(params string[] names)
    {
        foreach (var name in names)
        {
            _server.WithFile($"{QrfPath}/{name}", "SOURCE");
        }
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
