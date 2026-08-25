using QadAutomation.Core.Configuration;
using QadAutomation.Core.Transfer;

namespace QadAutomation.Core.Compile;

/// <summary>
/// Drives the QAD Progress procedure editor, one statement per report.
/// </summary>
/// <remarks>
/// <para>
/// This reproduces, keystroke for keystroke, what the operator does by hand:
/// open the editor, F4, type <c>compile ... save into ...</c>, F1. It is not the
/// sturdiest way to compile - Progress can be run in batch mode with a real exit
/// code - but the batch route needs the internals of the site's
/// <c>compile_editor</c> script, and reproducing the known-good procedure was
/// chosen over reverse-engineering an unknown one.
/// </para>
/// <para>
/// <b>The screen is never the verdict.</b> The editor has no exit status, and
/// its error text is drawn with cursor-positioning sequences that vary with
/// terminal size and Progress version. Scraping it for the word "error" would be
/// guesswork, and guessing optimistically means reporting a deploy that did not
/// happen. So success is decided entirely by the <c>.r</c> timestamp over SFTP,
/// which is the check the operator already trusts, and the captured screen is
/// only shown when that check says the compile failed.
/// </para>
/// </remarks>
internal sealed class ProgressEditorCompiler
{
    /// <summary>
    /// How long output must stop before the screen is considered settled.
    /// </summary>
    /// <remarks>
    /// The operator reports compiles finishing "very quickly", so this is tuned
    /// for a screen redraw rather than for the compile itself. Too short and a
    /// statement is typed into an editor that has not finished drawing; the cost
    /// of too long is only wasted seconds. Proven against the real editor.
    /// </remarks>
    private static readonly TimeSpan SettleFor = TimeSpan.FromMilliseconds(750);

    /// <summary>Upper bound on any single wait, so a hung editor cannot hang the tool.</summary>
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

    private readonly ISshShellFactory _shells;

    public ProgressEditorCompiler(ISshShellFactory shells) => _shells = shells;

    public IReadOnlyList<CompiledProgram> Compile(
        IReadOnlyList<PlannedEditorCompile> planned,
        EditorCompileSettings recipe,
        ISftpSession session,
        SshEndpoint endpoint,
        Action<string> report)
    {
        VerifySourcesExist(planned, session);

        // Read before anything is typed. Asking afterwards could only say what
        // the timestamp is, not whether it moved.
        var before = planned.ToDictionary(
            compile => compile,
            compile => session.LastWriteTime(compile.RemoteResult));

        var screens = RunEditor(planned, recipe, endpoint, report);

        return [.. planned.Select(compile =>
        {
            var after = session.LastWriteTime(compile.RemoteResult);
            var result = Moved(before[compile], after) ? CompileResult.Compiled : CompileResult.Failed;

            report($"  {(result == CompileResult.Compiled ? "compiled" : "FAILED  ")} {compile.File.FileName}");

            return new CompiledProgram(compile, result, screens.GetValueOrDefault(compile, string.Empty));
        })];
    }

    /// <summary>
    /// Runs the whole procedure, returning what the screen showed for each
    /// program.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Once per language, because one site's wrapper is opened per language and
    /// the other's is language-neutral - an empty language list runs it once.
    /// </para>
    /// <para>
    /// The screens are keyed by program and overwritten per language, so what
    /// survives is the last language's. Only the last one can have written the
    /// artefact being checked, so it is the relevant one - but a report that
    /// fails in the first language and succeeds in the second is a case this
    /// cannot show, and the docs say so.
    /// </para>
    /// </remarks>
    private Dictionary<PlannedEditorCompile, string> RunEditor(
        IReadOnlyList<PlannedEditorCompile> planned,
        EditorCompileSettings recipe,
        SshEndpoint endpoint,
        Action<string> report)
    {
        var screens = new Dictionary<PlannedEditorCompile, string>();

        // An empty list still runs once. The placeholder simply does not appear
        // in a language-neutral command, so substituting nothing is correct.
        var languages = recipe.Languages.Count > 0 ? recipe.Languages : [string.Empty];

        foreach (var language in languages)
        {
            RunEditorFor(planned, recipe, language, endpoint, report, screens);
        }

        return screens;
    }

    private void RunEditorFor(
        IReadOnlyList<PlannedEditorCompile> planned,
        EditorCompileSettings recipe,
        string language,
        SshEndpoint endpoint,
        Action<string> report,
        Dictionary<PlannedEditorCompile, string> screens)
    {
        var command = recipe.CommandFor(language);

        // Reopened per program where the wrapper takes only one; otherwise one
        // session serves the ticket, cleared between programs by a key in the
        // step list.
        var batches = recipe.RestartPerFile
            ? planned.Select(compile => (IReadOnlyList<PlannedEditorCompile>)[compile])
            : [planned];

        foreach (var batch in batches)
        {
            // Disposing the shell closes the channel, which is how these editors
            // are left - they have no exit key, and the manual procedure is to
            // close the window. Tied to `using` so it also happens on a throw.
            using var shell = _shells.Open(endpoint);

            shell.ReadUntilIdle(SettleFor, StepTimeout);

            if (recipe.WorkingDirectory is { } directory)
            {
                shell.Send($"cd {directory}" + ProgressKeys.Enter);
                shell.ReadUntilIdle(SettleFor, StepTimeout);
            }

            report($"Opening the editor: {command}");

            shell.Send(command + ProgressKeys.Enter);
            shell.ReadUntilIdle(SettleFor, StepTimeout);

            foreach (var compile in batch)
            {
                report($"  {compile.Statement}");
                screens[compile] = SendSteps(shell, recipe.Steps, compile.Statement);
            }
        }
    }

    /// <summary>
    /// Sends one program's step sequence, returning what came back last.
    /// </summary>
    /// <remarks>
    /// Every step is followed by a settle, including the last - the screen after
    /// the final keystroke is the one worth capturing, since that is where an
    /// error would appear.
    /// </remarks>
    private static string SendSteps(ISshShell shell, IReadOnlyList<EditorStep> steps, string statement)
    {
        var screen = string.Empty;

        foreach (var step in steps)
        {
            shell.Send(step switch
            {
                // No implied Return. Whether one follows is the wrapper's
                // business and is said out loud in the step list, because one
                // site needs it and the other is broken by it.
                EditorStep.Statement => statement,
                EditorStep.Enter => ProgressKeys.Enter,
                EditorStep.Go => ProgressKeys.Go,
                EditorStep.NewBuffer => ProgressKeys.NewBuffer,
                _ => throw new ArgumentOutOfRangeException(nameof(steps), step, "Unknown editor step.")
            });

            screen = shell.ReadUntilIdle(SettleFor, StepTimeout);
        }

        return screen;
    }

    /// <summary>
    /// Whether a compile happened, from the two timestamps.
    /// </summary>
    /// <remarks>
    /// A result that did not exist and now does counts, which is the ordinary
    /// case for a brand new report. Everything else needs the timestamp to have
    /// advanced: Progress leaves the old output exactly as it was when a compile
    /// fails, so "unchanged" and "failed" are the same observation.
    /// </remarks>
    internal static bool Moved(DateTimeOffset? before, DateTimeOffset? after) =>
        after is not null && (before is null || after > before);

    /// <summary>
    /// Confirms every <c>.p</c> is actually on the server before typing anything.
    /// </summary>
    /// <remarks>
    /// Without this, compiling a ticket that was never uploaded produces a screen
    /// full of Progress errors and a run that has to be read carefully to
    /// understand. The likely cause - forgetting <c>qad upload</c> - deserves to
    /// be said in one line.
    /// </remarks>
    private static void VerifySourcesExist(IReadOnlyList<PlannedEditorCompile> planned, ISftpSession session)
    {
        var missing = planned
            .Where(compile => !session.Exists(compile.RemoteFile))
            .Select(compile => compile.RemoteFile)
            .ToList();

        if (missing.Count > 0)
        {
            throw new TransferException(
                $"{missing.Count} file(s) are not on the server:{Environment.NewLine}" +
                string.Join(Environment.NewLine, missing.Select(p => "  - " + p)) +
                $"{Environment.NewLine}Run 'qad upload' first. Nothing was compiled.");
        }
    }
}
