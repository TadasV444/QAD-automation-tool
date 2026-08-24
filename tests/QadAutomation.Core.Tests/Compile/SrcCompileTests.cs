using QadAutomation.Core.Compile;
using QadAutomation.Core.Configuration;
using QadAutomation.Core.Tests.Transfer;
using QadAutomation.Core.Tickets;
using QadAutomation.Core.Transfer;

namespace QadAutomation.Core.Tests.Compile;

/// <summary>
/// Compiling SRC programs: manifest, batch script, two languages.
/// </summary>
public sealed class SrcCompileTests
{
    private const string SrcPath = "/appl/global/xrc";
    private const string LtRoot = "/appl/global/lt";
    private const string UsRoot = "/appl/global/us";
    private const string Manifest = "/appl/global/utcompil.wrk";

    private readonly FakeSftpServer _server = new FakeSftpServer()
        .WithDirectory(SrcPath, $"{LtRoot}/xx", $"{UsRoot}/xx");

    private readonly FakeSshShell _shell = new();

    public SrcCompileTests() => _shell.Server = _server;

    // --- planning ---------------------------------------------------------

    [Fact]
    public void A_program_is_compiled_into_one_folder_per_language()
    {
        var compile = Assert.Single(Plan("xxfoo.p").Using<PlannedManifestCompile>());

        Assert.Equal($"{SrcPath}/xxfoo.p", compile.RemoteFile);
        Assert.Equal($"{LtRoot}/xx/xxfoo.r", compile.Results["lt"]);
        Assert.Equal($"{UsRoot}/xx/xxfoo.r", compile.Results["us"]);
    }

    [Fact]
    public void The_output_folder_comes_from_the_first_two_letters_of_the_name()
    {
        // xx is a custom program; gp, ic, so and the rest work the same way.
        // Deliberately not a list in code or config - the compiler checks the
        // directory exists on the server, so nothing can go stale.
        var compile = Assert.Single(Plan("sofoo.p").Using<PlannedManifestCompile>());

        Assert.Equal($"{LtRoot}/so/sofoo.r", compile.Results["lt"]);
    }

    [Fact]
    public void A_name_too_short_to_have_a_prefix_is_skipped_rather_than_guessed()
    {
        var plan = Plan("x.p");

        Assert.Empty(plan.Using<PlannedManifestCompile>());
        Assert.Contains("prefix", Assert.Single(plan.Skipped).Reason, StringComparison.Ordinal);
    }

    // --- compiling --------------------------------------------------------

    [Fact]
    public void The_manifest_holds_one_bare_filename_per_line()
    {
        // The format most likely to be silently wrong: a path, a comma or a
        // missing newline all produce a script that compiles nothing while
        // looking like it ran.
        Uploaded("xxfoo.p", "xxbar.p");
        Builds("xxfoo", "xxbar");

        Compile("xxfoo.p", "xxbar.p");

        Assert.Equal("xxfoo.p\nxxbar.p\n", _server.Files[Manifest]);
    }

    [Fact]
    public void The_script_runs_once_per_language_from_its_working_directory()
    {
        Uploaded("xxfoo.p");
        Builds("xxfoo");

        Compile("xxfoo.p");

        Assert.Contains("cd /appl/global", _shell.Typed, StringComparison.Ordinal);
        Assert.Contains("./compile lt test", _shell.Typed, StringComparison.Ordinal);
        Assert.Contains("./compile us test", _shell.Typed, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_command_is_followed_by_an_enter_to_clear_the_scripts_dialog()
    {
        // The script raises a harmless warning and blocks on <OK>. Found on the
        // first real run: without this the second language's command was typed
        // into the dialog, so lt built, us did not, and the run failed.
        Uploaded("xxfoo.p");
        Builds("xxfoo");

        Compile("xxfoo.p");

        var afterCommands = _shell.Sent
            .SkipWhile(text => !text.StartsWith("./", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(
            [
                "./compile lt test" + ProgressKeys.Enter,
                ProgressKeys.Enter,
                "./compile us test" + ProgressKeys.Enter,
                ProgressKeys.Enter
            ],
            afterCommands);
    }

    [Fact]
    public void A_program_that_builds_in_both_languages_is_a_success()
    {
        Uploaded("xxfoo.p");
        Builds("xxfoo");

        var outcome = Compile("xxfoo.p");

        Assert.Equal(1, outcome.CompiledCount);
        Assert.False(outcome.NeedsAttention);
    }

    [Fact]
    public void A_program_that_builds_in_only_one_language_is_a_failure()
    {
        // Half the users would get the new program and half would keep the old
        // one, with nothing looking wrong. Worse than an outright failure, so
        // it is reported as one.
        Uploaded("xxfoo.p");
        _server.WithFile($"{LtRoot}/xx/xxfoo.r", "STALE");
        _server.WithFile($"{UsRoot}/xx/xxfoo.r", "STALE");

        // Only the 'us' run produces anything.
        _shell.Produces.Add([]);
        _shell.Produces.Add([$"{UsRoot}/xx/xxfoo.r"]);

        var outcome = Compile("xxfoo.p");

        Assert.Equal(1, outcome.FailedCount);
    }

    [Fact]
    public void The_failure_names_the_language_that_did_not_build()
    {
        Uploaded("xxfoo.p");
        _server.WithFile($"{LtRoot}/xx/xxfoo.r", "STALE");
        _server.WithFile($"{UsRoot}/xx/xxfoo.r", "STALE");
        _shell.Produces.Add([]);
        _shell.Produces.Add([$"{UsRoot}/xx/xxfoo.r"]);

        var lines = new List<string>();

        new QadCompiler(_shell, _server).Compile(Plan("xxfoo.p"), Endpoint, lines.Add);

        Assert.Contains(lines, line => line.Contains("lt did not build", StringComparison.Ordinal));
    }

    [Fact]
    public void Compiling_before_uploading_says_so_and_writes_no_manifest()
    {
        var message = Assert.Throws<TransferException>(() => Compile("xxfoo.p")).Message;

        Assert.Contains("qad upload", message, StringComparison.Ordinal);
        Assert.DoesNotContain(Manifest, _server.Files.Keys);
    }

    [Fact]
    public void A_missing_output_directory_stops_the_run_before_the_manifest_is_written()
    {
        // A program named outside the site's convention points at a folder that
        // does not exist. Caught here, it names the problem; caught later it
        // looks like a compile failure.
        Uploaded("zzfoo.p");

        var message = Assert.Throws<TransferException>(() => Compile("zzfoo.p")).Message;

        Assert.Contains($"{LtRoot}/zz", message, StringComparison.Ordinal);
        Assert.Contains("first two letters", message, StringComparison.Ordinal);
        Assert.DoesNotContain(Manifest, _server.Files.Keys);
    }

    [Fact]
    public void A_mixed_ticket_compiles_both_kinds()
    {
        Uploaded("xxfoo.p");
        Builds("xxfoo");

        var qrfPath = "/appl/desktop/test/reports";
        _server.WithDirectory(qrfPath).WithFile($"{qrfPath}/rep.p", "SOURCE");

        // The QRF editor's F1 is the third Go of the run - the two script runs
        // come first, because SRC is compiled before the editor is opened.
        _shell.Produces.Add([$"{qrfPath}/rep.r"]);

        var ticket = new TicketFolder("Ticket 9999555", @"C:\tasks\T",
        [
            new ProgramFile(ProgramKind.Src, @"C:\tasks\T\SRC\xxfoo.p"),
            new ProgramFile(ProgramKind.Qrf, @"C:\tasks\T\QRF\rep.p")
        ]);

        var outcome = new QadCompiler(_shell, _server)
            .Compile(CompilePlan.Create(ticket, Environment(qrf: qrfPath), "pilot"), Endpoint);

        Assert.Equal(2, outcome.CompiledCount);
        Assert.Empty(outcome.Skipped);
    }

    // --- helpers ---------------------------------------------------------

    private static readonly SshEndpoint Endpoint = new("qad.example", 22, "mfg", "hunter2", null);

    private void Uploaded(params string[] names)
    {
        foreach (var name in names)
        {
            _server.WithFile($"{SrcPath}/{name}", "SOURCE");
        }
    }

    /// <summary>
    /// Makes the script produce both languages' output for these programs.
    /// </summary>
    /// <remarks>
    /// The first F1 stands for <c>./compile lt</c> and the second for
    /// <c>./compile us</c>, matching the order the languages are configured in.
    /// </remarks>
    private void Builds(params string[] baseNames)
    {
        _shell.Produces.Add([.. baseNames.Select(n => $"{LtRoot}/xx/{n}.r")]);
        _shell.Produces.Add([.. baseNames.Select(n => $"{UsRoot}/xx/{n}.r")]);
    }

    private CompileOutcome Compile(params string[] names) =>
        new QadCompiler(_shell, _server).Compile(Plan(names), Endpoint);

    private static CompilePlan Plan(params string[] names) =>
        CompilePlan.Create(
            new TicketFolder(
                "Ticket 9999555",
                @"C:\tasks\T",
                [.. names.Select(n => new ProgramFile(ProgramKind.Src, Path.Combine(@"C:\tasks\T\SRC", n)))]),
            Environment(),
            "pilot");

    private static QadEnvironment Environment(string? qrf = null) =>
        new(
            "TEST",
            false,
            Endpoint,
            new RemotePaths(SrcPath, qrf),
            new CompileSettings(
                qrf is null
                    ? null
                    : new QrfCompileSettings(new EditorCompileSettings("compile_editor us test", EditorCompileSettings.DefaultStatementTemplate), null),
                new SrcCompileSettings(
                    new ManifestCompileSettings(
                        Manifest,
                        "/appl/global",
                        "./compile {language} test",
                        new Dictionary<string, string> { ["lt"] = LtRoot, ["us"] = UsRoot }),
                    null)));
}
