using QadAutomation.Cli;
using QadAutomation.Core.Tests.Compile;
using QadAutomation.Core.Tests.Transfer;
using QadAutomation.Core.Tests.Vpn;
using QadAutomation.Core.Vpn;

namespace QadAutomation.Core.Tests.Cli;

/// <summary>
/// The guided flow, driven through the real entry point with scripted answers.
/// </summary>
/// <remarks>
/// Answers are supplied as lines of text, exactly as a person would type them -
/// so a test reads as a transcript of the session it describes.
/// </remarks>
public sealed class LauncherCommandTests : IDisposable
{
    private const string SrcPath = "/appl/qad/global/xrc";
    private const string QrfPath = "/appl/qad/desktop/test/reports";
    private const string VpnName = "PilotVpn";

    private readonly string _root;
    private readonly string _configPath;
    private readonly FakeRasDial _rasDial = new();
    private readonly FakeSftpServer _server = new FakeSftpServer().WithDirectory(SrcPath, QrfPath);
    private readonly FakeSshShell _shell = new();

    public LauncherCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "qad-menu-" + Guid.NewGuid().ToString("N"));

        Ticket("Ticket #100001");
        Ticket("Ticket #100002");

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
                  "compile": { "qrf": { "editor": { "editorCommand": "{{QrfPath}}/compile_editor us test" } } }
                },
                "environments": [
                  { "name": "TEST" },
                  { "name": "PROD", "aliases": ["euro"] }
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
    public void The_whole_flow_ends_in_an_upload_and_a_compile()
    {
        Uploaded("rep_b.p");
        _shell.Produces.Add([$"{QrfPath}/rep_b.r"]);

        // One client, so that question is stated rather than asked: environment
        // 1 (TEST), ticket 1, then yes.
        var (exitCode, output, _) = Run("1", "1", "y");

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Contains("Ticket #100001", output);
        Assert.Contains("1 replaced", output);
        Assert.Contains("1 compiled, 0 failed", output);
    }

    [Fact]
    public void The_plan_is_shown_and_confirmed_before_anything_connects()
    {
        // Answering every question and then declining must leave the server
        // untouched - the operator has seen the plan and said no.
        var (exitCode, output, _) = Run("1", "1", "n");

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Contains("file(s) to upload", output);
        Assert.Contains("Cancelled", output);

        Assert.Empty(_server.Files);
        Assert.Empty(_rasDial.ConnectCalls);
    }

    [Fact]
    public void Production_needs_the_environment_typed_out()
    {
        // 'y' is what a tired person presses without reading, so production
        // will not take it - the same reasoning as --yes on the command line.
        var (exitCode, output, _) = Run("2", "1", "y");

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Contains("Cancelled", output);
        Assert.Empty(_server.Files);
    }

    [Fact]
    public void Production_proceeds_when_its_name_is_typed()
    {
        Uploaded("rep_b.p");
        _shell.Produces.Add([$"{QrfPath}/rep_b.r"]);

        var (exitCode, output, _) = Run("2", "1", "PROD");

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Contains("** PRODUCTION **", output);
        Assert.Contains("1 compiled", output);
    }

    [Fact]
    public void The_environment_picker_shows_aliases_and_marks_production()
    {
        var (_, output, _) = Run("", "");

        Assert.Contains("PROD (euro)", output);
        Assert.Contains("** PRODUCTION **", output);
    }

    [Fact]
    public void A_blank_answer_cancels_without_touching_anything()
    {
        var (exitCode, output, _) = Run("");

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Contains("Cancelled", output);
        Assert.Empty(_server.Files);
    }

    [Fact]
    public void Running_out_of_input_cancels_rather_than_hanging()
    {
        // What happens when stdin is not a console and nobody is there to
        // answer. Cancelling is the only safe reading of silence.
        var (exitCode, output, _) = Run();

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Contains("Cancelled", output);
    }

    [Fact]
    public void A_number_that_is_not_on_the_list_asks_again()
    {
        // A typo in a menu should cost a keystroke, not the session.
        var (exitCode, output, _) = Run("9", "notanumber", "1", "1", "n");

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Contains("'9' is not one of the numbers above", output);
        Assert.Contains("'notanumber' is not one of the numbers above", output);
        Assert.Contains("Cancelled", output);
    }

    [Fact]
    public void A_single_option_is_stated_rather_than_asked_about()
    {
        // One client, so there is nothing to choose. Asking would invite the
        // operator to wonder what the alternatives were.
        var (_, output, _) = Run("1", "1", "n");

        Assert.Contains("Client: Pilot Client", output);
        Assert.DoesNotContain("1) Pilot Client", output);
    }

    [Fact]
    public void An_empty_working_folder_says_so_instead_of_offering_nothing()
    {
        Directory.Delete(Path.Combine(_root, "tasks"), recursive: true);
        Directory.CreateDirectory(Path.Combine(_root, "tasks"));

        var (exitCode, output, _) = Run("1");

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Contains("No ticket folders found", output);
    }

    [Fact]
    public void Enter_at_the_end_goes_back_to_the_menu()
    {
        // The reflex at any prompt is Enter, and a menu that closed on it would
        // send the operator back to relaunching the program - which is the whole
        // thing this was built to stop. Going round again is the default; only
        // leaving has to be typed.
        var (exitCode, output, _) = Run("1", "1", "n", "", "1", "2", "n", "q");

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Contains("Ticket #100001", output);
        Assert.Contains("Ticket #100002", output);
        Assert.Equal(2, Occurrences(output, "QAD Compile Automation Tool"));
    }

    [Fact]
    public void Quitting_takes_a_word_rather_than_a_keystroke()
    {
        // Going round again costs one keystroke; quitting by accident costs a
        // relaunch and everything on screen.
        var (_, output, _) = Run("1", "1", "n", "q");

        Assert.Equal(1, Occurrences(output, "QAD Compile Automation Tool"));
    }

    [Fact]
    public void Anything_unrecognised_at_the_end_keeps_the_menu_open()
    {
        // Erring towards staying: an unexpected keystroke should not be read as
        // a decision to close.
        var (_, output, _) = Run("1", "1", "n", "wat", "1", "1", "n", "q");

        Assert.Equal(2, Occurrences(output, "QAD Compile Automation Tool"));
    }

    [Fact]
    public void Saying_yes_at_the_end_goes_back_to_the_start()
    {
        // A ticket is rarely the only one. Relaunching to deploy the next is
        // the friction the menu exists to remove.
        var (exitCode, output, _) = Run("1", "1", "n", "y", "1", "2", "n", "q");

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Contains("Ticket #100001", output);
        Assert.Contains("Ticket #100002", output);

        // Two passes through the flow, so the banner appears twice.
        Assert.Equal(2, Occurrences(output, "QAD Compile Automation Tool"));
    }

    [Fact]
    public void A_failure_the_tool_can_explain_keeps_the_menu_open()
    {
        // The commonest is a tunnel that is not up: fixed in ten seconds, and
        // the operator wants to retry rather than start the program again.
        _rasDial.ConnectFailureCode = 800;

        var (_, output, error) = Run("1", "1", "y", "q");

        Assert.Contains("main menu", output);
        Assert.NotEqual(string.Empty, error);
    }

    [Fact]
    public void The_exit_code_is_the_last_runs()
    {
        _rasDial.ConnectFailureCode = 800;

        var (exitCode, _, _) = Run("1", "1", "y", "n");

        Assert.Equal(ExitCode.VpnError, exitCode);
    }

    [Fact]
    public void A_supplied_input_is_never_left_waiting_for_a_keypress()
    {
        // A double-clicked shortcut gets its own window and needs holding open
        // to be read. A test - or a script feeding answers - has nobody to
        // press the key, so the pause must not apply when the input was given.
        var (_, output, _) = Run("1", "1", "n");

        Assert.DoesNotContain("Press Enter to close", output);
    }

    [Fact]
    public void The_password_never_appears_in_the_output()
    {
        Uploaded("rep_b.p");
        _shell.Produces.Add([$"{QrfPath}/rep_b.r"]);

        var (_, output, error) = Run("1", "1", "y");

        Assert.DoesNotContain("hunter2", output + error);
    }

    // --- helpers ---------------------------------------------------------

    private static int Occurrences(string haystack, string needle) =>
        haystack.Split(needle).Length - 1;

    private void Ticket(string name)
    {
        var qrf = Path.Combine(_root, "tasks", name, "QRF");
        Directory.CreateDirectory(qrf);
        File.WriteAllText(Path.Combine(qrf, "rep_b.p"), "QRF B");
    }

    private void Uploaded(params string[] names)
    {
        foreach (var name in names)
        {
            _server.WithFile($"{QrfPath}/{name}", "SOURCE");
        }
    }

    /// <summary>Runs the guided flow with <paramref name="answers"/> typed in turn.</summary>
    private (int ExitCode, string Output, string Error) Run(params string[] answers)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var input = new StringReader(string.Join(System.Environment.NewLine, answers));

        var exitCode = new CommandLineApplication(
                output, error, new VpnConnectorFactory(_rasDial), _server, _shell, input)
            .Run(["--config", _configPath]);

        return (exitCode, output.ToString(), error.ToString());
    }
}
