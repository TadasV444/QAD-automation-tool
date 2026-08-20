using QadAutomation.Core.Compile;
using QadAutomation.Core.Configuration;
using QadAutomation.Core.Tests.Transfer;
using QadAutomation.Core.Tickets;
using QadAutomation.Core.Transfer;

namespace QadAutomation.Core.Tests.Compile;

public sealed class ProgressEditorCompilerTests
{
    private const string QrfPath = "/appl/desktop/test/reports";

    private readonly FakeSftpServer _server = new FakeSftpServer().WithDirectory(QrfPath);
    private readonly FakeSshShell _shell = new();

    public ProgressEditorCompilerTests() => _shell.Server = _server;

    [Fact]
    public void A_compile_that_updates_the_r_file_is_a_success()
    {
        Uploaded("rep.p");
        _shell.Produces.Add($"{QrfPath}/rep.r");

        var outcome = Compile("rep.p");

        Assert.Equal(1, outcome.CompiledCount);
        Assert.Equal(0, outcome.FailedCount);
    }

    [Fact]
    public void A_compile_that_leaves_the_r_file_alone_is_a_failure()
    {
        // Progress leaves the previous .r exactly as it was when it rejects the
        // source, so "unchanged" and "failed" are the same observation. This is
        // the whole verification mechanism.
        Uploaded("rep.p");
        _server.WithFile($"{QrfPath}/rep.r", "PREVIOUSLY COMPILED");

        var outcome = Compile("rep.p");

        Assert.Equal(0, outcome.CompiledCount);
        Assert.Equal(1, outcome.FailedCount);
    }

    [Fact]
    public void A_first_time_compile_counts_even_though_there_was_no_r_file()
    {
        Uploaded("brand_new.p");
        _shell.Produces.Add($"{QrfPath}/brand_new.r");

        Assert.Equal(1, Compile("brand_new.p").CompiledCount);
    }

    [Fact]
    public void The_screen_is_never_what_decides_the_verdict()
    {
        // A compiler that scraped the screen would report failure here. The .r
        // moved, so the compile happened - whatever Progress chose to print.
        Uploaded("rep.p");
        _shell.Produces.Add($"{QrfPath}/rep.r");
        _shell.Screen = "** Unable to understand after -- \"error\". (247)";

        Assert.Equal(1, Compile("rep.p").CompiledCount);
    }

    [Fact]
    public void The_editor_is_opened_once_and_each_report_gets_its_own_statement()
    {
        Uploaded("a.p", "b.p");
        _shell.Produces.AddRange([$"{QrfPath}/a.r", $"{QrfPath}/b.r"]);

        Compile("a.p", "b.p");

        Assert.Equal(2, Occurrences(_shell.Typed, "compile "));
        Assert.Contains($"compile {QrfPath}/a.p save into {QrfPath}.", _shell.Typed, StringComparison.Ordinal);
        Assert.Contains($"compile {QrfPath}/b.p save into {QrfPath}.", _shell.Typed, StringComparison.Ordinal);

        // Opened once, not once per file.
        Assert.Equal(1, Occurrences(_shell.Typed, "compile_editor"));
    }

    [Fact]
    public void The_buffer_is_cleared_before_each_statement_and_run_after_it()
    {
        Uploaded("a.p", "b.p");
        _shell.Produces.AddRange([$"{QrfPath}/a.r", $"{QrfPath}/b.r"]);

        Compile("a.p", "b.p");

        // F4 ... statement ... F1, twice. A statement typed into an uncleared
        // buffer would compile the previous report again.
        var keys = _shell.Sent
            .Where(s => s == ProgressKeys.NewBuffer || s == ProgressKeys.Go)
            .ToList();

        Assert.Equal(
            [ProgressKeys.NewBuffer, ProgressKeys.Go, ProgressKeys.NewBuffer, ProgressKeys.Go],
            keys);
    }

    [Fact]
    public void One_report_failing_does_not_hide_the_others_succeeding()
    {
        Uploaded("a.p", "b.p", "c.p");
        _server.WithFile($"{QrfPath}/b.r", "STALE");

        // b produces nothing: its .r stays exactly where it was.
        _shell.Produces.AddRange([$"{QrfPath}/a.r", null, $"{QrfPath}/c.r"]);

        var outcome = Compile("a.p", "b.p", "c.p");

        Assert.Equal(2, outcome.CompiledCount);
        Assert.Equal("b.p", Assert.Single(outcome.Failures).Planned.File.FileName);
    }

    [Fact]
    public void Compiling_a_ticket_that_was_never_uploaded_says_so()
    {
        // Without this the operator gets a screen full of Progress errors for
        // what is almost always one forgotten command.
        var message = Assert.Throws<TransferException>(() => Compile("never_uploaded.p")).Message;

        Assert.Contains("qad upload", message, StringComparison.Ordinal);
        Assert.Contains("Nothing was compiled", message, StringComparison.Ordinal);

        // And it fails before the editor is ever opened.
        Assert.Null(_shell.OpenedFor);
    }

    [Fact]
    public void The_shell_is_always_closed()
    {
        // It has no exit key, so closing the channel is the only way out.
        Uploaded("rep.p");
        _shell.Produces.Add($"{QrfPath}/rep.r");

        Compile("rep.p");

        Assert.True(_shell.IsDisposed);
    }

    [Fact]
    public void Skipped_programs_survive_into_the_outcome()
    {
        Uploaded("rep.p");
        _shell.Produces.Add($"{QrfPath}/rep.r");

        var ticket = new TicketFolder("Ticket 9999555", @"C:\tasks\T",
        [
            new ProgramFile(ProgramKind.Qrf, @"C:\tasks\T\QRF\rep.p"),
            new ProgramFile(ProgramKind.Src, @"C:\tasks\T\SRC\prog.p")
        ]);

        var outcome = new ProgressEditorCompiler(_shell, _server)
            .Compile(CompilePlan.Create(ticket, Environment(), "pilot"), Endpoint);

        Assert.Equal(1, outcome.CompiledCount);
        Assert.Single(outcome.Skipped);
        Assert.True(outcome.NeedsAttention);
    }

    [Fact]
    public void An_empty_plan_does_not_connect()
    {
        var outcome = new ProgressEditorCompiler(_shell, _server).Compile(
            CompilePlan.Create(new TicketFolder("T", "p", []), Environment(), "pilot"),
            Endpoint);

        Assert.Empty(outcome.Programs);
        Assert.Null(_server.ConnectedTo);
        Assert.Null(_shell.OpenedFor);
    }

    [Fact]
    public void The_password_never_appears_in_the_progress_output()
    {
        Uploaded("rep.p");
        _shell.Produces.Add($"{QrfPath}/rep.r");

        var lines = new List<string>();

        new ProgressEditorCompiler(_shell, _server)
            .Compile(Plan("rep.p"), Endpoint, lines.Add);

        Assert.DoesNotContain(lines, line => line.Contains("hunter2", StringComparison.Ordinal));
    }

    // --- helpers ---------------------------------------------------------

    private static readonly SshEndpoint Endpoint = new("qad.example", 22, "mfg", "hunter2", null);

    /// <summary>Puts the given reports on the server, as <c>qad upload</c> would.</summary>
    private void Uploaded(params string[] names)
    {
        foreach (var name in names)
        {
            _server.WithFile($"{QrfPath}/{name}", "SOURCE");
        }
    }

    private CompileOutcome Compile(params string[] names) =>
        new ProgressEditorCompiler(_shell, _server).Compile(Plan(names), Endpoint);

    private static CompilePlan Plan(params string[] names) =>
        CompilePlan.Create(
            new TicketFolder(
                "Ticket 9999555",
                @"C:\tasks\T",
                [.. names.Select(n => new ProgramFile(ProgramKind.Qrf, Path.Combine(@"C:\tasks\T\QRF", n)))]),
            Environment(),
            "pilot");

    private static int Occurrences(string haystack, string needle) =>
        haystack.Split(needle).Length - 1;

    private static QadEnvironment Environment() =>
        new(
            "TEST",
            false,
            Endpoint,
            new RemotePaths("/appl/global/xrc", QrfPath),
            new CompileSettings(
                new QrfCompileSettings("compile_editor us test", QrfCompileSettings.DefaultStatementTemplate),
                null));
}
