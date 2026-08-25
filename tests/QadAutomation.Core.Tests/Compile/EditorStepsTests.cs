using QadAutomation.Core.Compile;
using QadAutomation.Core.Configuration;
using QadAutomation.Core.Tests.Transfer;
using QadAutomation.Core.Tickets;

namespace QadAutomation.Core.Tests.Compile;

/// <summary>
/// The editor procedure's keystroke sequence, which differs per site.
/// </summary>
/// <remarks>
/// Two real wrappers are represented here. One is entered with F4 and run with
/// F1, taking many programs in a session. The other is entered with Return, run
/// with F1 twice, takes one program per session, and is opened once per
/// language. Both are the same code driven by different config.
/// </remarks>
public sealed class EditorStepsTests
{
    private const string QrfPath = "/appl/test/reports";

    private readonly FakeSftpServer _server = new FakeSftpServer().WithDirectory(QrfPath);
    private readonly FakeSshShell _shell = new();

    public EditorStepsTests() => _shell.Server = _server;

    [Fact]
    public void The_first_sites_sequence_is_unchanged_by_the_defaults()
    {
        // Proven against a real editor, so the defaults must still produce it
        // exactly: F4, the statement, Return, F1.
        Uploaded("rep.p");
        _shell.Produces.Add([$"{QrfPath}/rep.r"]);

        Compile(Default());

        Assert.Equal(
            [
                ProgressKeys.NewBuffer,
                $"compile {QrfPath}/rep.p save into {QrfPath}.",
                ProgressKeys.Enter,
                ProgressKeys.Go
            ],
            KeysAfterEditorOpens("compile_editor"));
    }

    [Fact]
    public void The_second_sites_sequence_types_no_return_after_the_path()
    {
        // Return opens the input window; F1 leaves it and F1 again compiles. A
        // Return after the path would be read as a second, empty entry.
        Uploaded("rep.p");
        _shell.Produces.Add([$"{QrfPath}/rep.r"]);

        Compile(SecondSite());

        Assert.Equal(
            [
                ProgressKeys.Enter,
                $"{QrfPath}/rep.p",
                ProgressKeys.Go,
                ProgressKeys.Go
            ],
            KeysAfterEditorOpens("./compile -s"));
    }

    [Fact]
    public void A_language_aware_wrapper_is_opened_once_per_language()
    {
        Uploaded("rep.p");
        _shell.Produces.Add([$"{QrfPath}/rep.r"]);

        Compile(SecondSite());

        Assert.Contains("./compile -s lt", _shell.Typed, StringComparison.Ordinal);
        Assert.Contains("./compile -s us", _shell.Typed, StringComparison.Ordinal);
    }

    [Fact]
    public void A_relative_command_is_run_from_its_working_directory()
    {
        Uploaded("rep.p");
        _shell.Produces.Add([$"{QrfPath}/rep.r"]);

        Compile(SecondSite());

        Assert.Contains($"cd {QrfPath}", _shell.Typed, StringComparison.Ordinal);
    }

    [Fact]
    public void An_absolute_command_is_not_preceded_by_a_cd()
    {
        Uploaded("rep.p");
        _shell.Produces.Add([$"{QrfPath}/rep.r"]);

        Compile(Default());

        Assert.DoesNotContain("cd ", _shell.Typed, StringComparison.Ordinal);
    }

    [Fact]
    public void A_wrapper_that_takes_one_program_is_reopened_for_the_next()
    {
        // Two reports, two languages, one program per session: four openings.
        Uploaded("a.p", "b.p");
        _shell.Produces.Add([$"{QrfPath}/a.r", $"{QrfPath}/b.r"]);

        Compile(SecondSite(), "a.p", "b.p");

        Assert.Equal(4, Occurrences(_shell.Typed, "./compile -s"));
    }

    [Fact]
    public void A_wrapper_that_takes_many_keeps_one_session_for_them_all()
    {
        Uploaded("a.p", "b.p");
        _shell.Produces.Add([$"{QrfPath}/a.r", $"{QrfPath}/b.r"]);

        Compile(Default(), "a.p", "b.p");

        Assert.Equal(1, Occurrences(_shell.Typed, "compile_editor"));
    }

    // --- helpers ---------------------------------------------------------

    private static readonly SshEndpoint Endpoint = new("qad.example", 22, "mfg", "hunter2", null);

    /// <summary>The first site: absolute command, F4/F1, many per session.</summary>
    private static EditorCompileSettings Default() =>
        new(
            "/appl/test/reports/compile_editor us test",
            null,
            [],
            EditorCompileSettings.DefaultSteps,
            false,
            EditorCompileSettings.DefaultStatementTemplate);

    /// <summary>The second site: cd, per language, Return/F1/F1, one per session.</summary>
    private static EditorCompileSettings SecondSite() =>
        new(
            "./compile -s {language}",
            QrfPath,
            ["lt", "us"],
            [EditorStep.Enter, EditorStep.Statement, EditorStep.Go, EditorStep.Go],
            true,
            "{remoteFile}");

    private void Uploaded(params string[] names)
    {
        foreach (var name in names)
        {
            _server.WithFile($"{QrfPath}/{name}", "SOURCE");
        }
    }

    private void Compile(EditorCompileSettings recipe, params string[] names)
    {
        var files = names.Length > 0 ? names : ["rep.p"];

        var ticket = new TicketFolder(
            "Ticket 9999555",
            @"C:\tasks\T",
            [.. files.Select(n => new ProgramFile(ProgramKind.Qrf, Path.Combine(@"C:\tasks\T\QRF", n)))]);

        var environment = new QadEnvironment(
            "TEST",
            false,
            Endpoint,
            new RemotePaths(null, QrfPath),
            new CompileSettings(new QrfCompileSettings(recipe, null), null));

        new QadCompiler(_shell, _server)
            .Compile(CompilePlan.Create(ticket, environment, "pilot"), Endpoint);
    }

    /// <summary>
    /// One program's sequence: what was sent between the editor launching and
    /// it launching again.
    /// </summary>
    /// <remarks>
    /// Bounded at both ends because a per-language wrapper opens more than
    /// once, and a test about the key order should not also be asserting how
    /// many runs there are - that is its own test.
    /// </remarks>
    private IReadOnlyList<string> KeysAfterEditorOpens(string command) =>
        [.. _shell.Sent
            .SkipWhile(sent => !sent.Contains(command, StringComparison.Ordinal))
            .Skip(1)
            // The next session begins with its cd, before the command itself.
            .TakeWhile(sent =>
                !sent.Contains(command, StringComparison.Ordinal) &&
                !sent.StartsWith("cd ", StringComparison.Ordinal))];

    private static int Occurrences(string haystack, string needle) =>
        haystack.Split(needle).Length - 1;
}
