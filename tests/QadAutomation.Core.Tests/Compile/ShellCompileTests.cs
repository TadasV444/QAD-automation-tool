using QadAutomation.Core.Compile;
using QadAutomation.Core.Configuration;
using QadAutomation.Core.Tests.Transfer;
using QadAutomation.Core.Tickets;
using QadAutomation.Core.Transfer;

namespace QadAutomation.Core.Tests.Compile;

/// <summary>
/// Compiling by running one shell command - the site's own build script.
/// </summary>
public sealed class ShellCompileTests
{
    private const string SrcPath = "/appl/mfg/src/us/{prefix}";
    private const string QrfPath = "/appl/pro/reports";
    private const string WorkingDirectory = "/appl/apps";

    private readonly FakeSftpServer _server = new FakeSftpServer()
        .WithDirectory("/appl/mfg/src/us/xx", QrfPath);

    private readonly FakeSshShell _shell = new();

    public ShellCompileTests() => _shell.Server = _server;

    [Fact]
    public void The_command_runs_once_for_the_whole_ticket_from_its_directory()
    {
        // Unlike the editor there is nothing per file: the script finds its own
        // work, so two programs still mean one command.
        Uploaded("/appl/mfg/src/us/xx/xxfoo.p", "/appl/mfg/src/us/xx/xxbar.p");
        _shell.ExitCode = 0;

        Compile(Src("xxfoo.p"), Src("xxbar.p"));

        Assert.Contains($"cd {WorkingDirectory}", _shell.Typed, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(_shell.Typed, "./build customizations"));
    }

    [Fact]
    public void A_zero_exit_code_is_a_success_when_there_is_nothing_else_to_check()
    {
        Uploaded("/appl/mfg/src/us/xx/xxfoo.p");
        _shell.ExitCode = 0;

        var outcome = Compile(Src("xxfoo.p"));

        Assert.Equal(1, outcome.CompiledCount);
    }

    [Fact]
    public void A_non_zero_exit_code_fails_every_program_in_the_batch()
    {
        // One command built them all, so there is no per-file signal to divide
        // them by. Reporting all as failed is the honest reading.
        Uploaded("/appl/mfg/src/us/xx/xxfoo.p", "/appl/mfg/src/us/xx/xxbar.p");
        _shell.ExitCode = 2;

        var outcome = Compile(Src("xxfoo.p"), Src("xxbar.p"));

        Assert.Equal(2, outcome.FailedCount);
    }

    [Fact]
    public void A_shell_that_reports_no_exit_code_at_all_is_a_failure()
    {
        // The marker never came back, so nothing is known. Treating silence as
        // success is the one direction of error that must not happen.
        Uploaded("/appl/mfg/src/us/xx/xxfoo.p");
        _shell.ExitCode = null;

        Assert.Equal(1, Compile(Src("xxfoo.p")).FailedCount);
    }

    [Fact]
    public void The_artefact_overrules_a_zero_exit_code()
    {
        // A build script that reports success while writing nothing is a real
        // possibility, and the failure it causes is silent. Where the site tells
        // us what to look at, that decides.
        Uploaded("/appl/mfg/src/us/xx/xxfoo.p");
        _server.WithFile("/appl/mfg/build/xx/xxfoo.r", "STALE");
        _shell.ExitCode = 0;

        var outcome = Compile(Src("xxfoo.p"), resultPath: "/appl/mfg/build/{prefix}/{name}.r");

        Assert.Equal(1, outcome.FailedCount);
    }

    [Fact]
    public void The_artefact_moving_is_a_success()
    {
        Uploaded("/appl/mfg/src/us/xx/xxfoo.p");
        _shell.ExitCode = 0;
        _shell.Produces.Add(["/appl/mfg/build/xx/xxfoo.r"]);

        var outcome = Compile(Src("xxfoo.p"), resultPath: "/appl/mfg/build/{prefix}/{name}.r");

        Assert.Equal(1, outcome.CompiledCount);
    }

    [Fact]
    public void Each_kind_runs_its_own_command()
    {
        // The two builds are separate scripts at this site, so a mixed ticket
        // needs both - and neither should be run for a ticket that has only the
        // other kind.
        Uploaded("/appl/mfg/src/us/xx/xxfoo.p", $"{QrfPath}/rep.p");
        _shell.ExitCode = 0;

        var ticket = new TicketFolder("Ticket 9999555", @"C:\tasks\T",
        [
            new ProgramFile(ProgramKind.Src, @"C:\tasks\T\SRC\xxfoo.p"),
            new ProgramFile(ProgramKind.Qrf, @"C:\tasks\T\QRF\rep.p")
        ]);

        var outcome = new QadCompiler(_shell, _server)
            .Compile(CompilePlan.Create(ticket, Environment(), "pilot"), Endpoint);

        Assert.Equal(2, outcome.CompiledCount);
        Assert.Contains("./build customizations", _shell.Typed, StringComparison.Ordinal);
        Assert.Contains("./build reports", _shell.Typed, StringComparison.Ordinal);
    }

    [Fact]
    public void Compiling_before_uploading_says_so()
    {
        var message = Assert.Throws<TransferException>(() => Compile(Src("xxfoo.p"))).Message;

        Assert.Contains("qad upload", message, StringComparison.Ordinal);
        Assert.Null(_shell.OpenedFor);
    }

    [Fact]
    public void The_per_prefix_source_path_is_resolved_the_same_way_the_upload_resolved_it()
    {
        // Upload and compile deriving the destination separately is how they
        // come to disagree - and the symptom would be "not on the server" for a
        // file that plainly is.
        Uploaded("/appl/mfg/src/us/xx/xxfoo.p");
        _shell.ExitCode = 0;

        var compile = Assert.Single(Plan(Src("xxfoo.p")).Using<PlannedShellCompile>());

        Assert.Equal("/appl/mfg/src/us/xx/xxfoo.p", compile.RemoteFile);
    }

    // --- helpers ---------------------------------------------------------

    private static readonly SshEndpoint Endpoint = new("qad.example", 22, "mfg", "hunter2", null);

    private static ProgramFile Src(string name) =>
        new(ProgramKind.Src, Path.Combine(@"C:\tasks\T\SRC", name));

    private void Uploaded(params string[] remotePaths)
    {
        foreach (var path in remotePaths)
        {
            _server.WithFile(path, "SOURCE");
        }
    }

    private CompileOutcome Compile(ProgramFile file, string? resultPath = null) =>
        new QadCompiler(_shell, _server).Compile(Plan(file, resultPath), Endpoint);

    private CompileOutcome Compile(params ProgramFile[] files) =>
        new QadCompiler(_shell, _server).Compile(Plan(files), Endpoint);

    private static CompilePlan Plan(ProgramFile file, string? resultPath) =>
        CompilePlan.Create(
            new TicketFolder("Ticket 9999555", @"C:\tasks\T", [file]),
            Environment(resultPath),
            "pilot");

    private static CompilePlan Plan(params ProgramFile[] files) =>
        CompilePlan.Create(
            new TicketFolder("Ticket 9999555", @"C:\tasks\T", files),
            Environment(),
            "pilot");

    private static int Occurrences(string haystack, string needle) =>
        haystack.Split(needle).Length - 1;

    private static QadEnvironment Environment(string? resultPath = null) =>
        new(
            "TEST",
            false,
            Endpoint,
            new RemotePaths(SrcPath, QrfPath),
            new CompileSettings(
                new QrfCompileSettings(
                    null,
                    new ShellCompileSettings(WorkingDirectory, "./build reports -v", null)),
                new SrcCompileSettings(
                    null,
                    new ShellCompileSettings(WorkingDirectory, "./build customizations -v", resultPath))));
}
