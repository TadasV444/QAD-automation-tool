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
        CompilePlan plan,
        QrfCompileSettings recipe,
        ISftpSession session,
        SshEndpoint endpoint,
        Action<string> report)
    {
        VerifySourcesExist(plan, session);

        // Read before anything is typed. Asking afterwards could only say what
        // the timestamp is, not whether it moved.
        var before = plan.Qrf.ToDictionary(
            compile => compile,
            compile => session.LastWriteTime(compile.RemoteResult));

        var screens = RunEditor(plan, recipe, endpoint, report);

        return [.. plan.Qrf.Select(compile =>
        {
            var after = session.LastWriteTime(compile.RemoteResult);
            var result = Moved(before[compile], after) ? CompileResult.Compiled : CompileResult.Failed;

            report($"  {(result == CompileResult.Compiled ? "compiled" : "FAILED  ")} {compile.File.FileName}");

            return new CompiledProgram(compile, result, screens.GetValueOrDefault(compile, string.Empty));
        })];
    }

    /// <summary>
    /// Opens the editor and types every statement into it, returning what the
    /// screen showed for each.
    /// </summary>
    /// <remarks>
    /// One editor session for the whole ticket, with F4 between statements to
    /// clear the buffer. That mirrors the manual procedure, where F4 is what
    /// makes the window ready to accept a statement.
    /// </remarks>
    private Dictionary<PlannedQrfCompile, string> RunEditor(
        CompilePlan plan,
        QrfCompileSettings recipe,
        SshEndpoint endpoint,
        Action<string> report)
    {
        var screens = new Dictionary<PlannedQrfCompile, string>();

        // Disposing the shell closes the channel, which is how this editor is
        // left - it has no exit key here, and the manual procedure is to close
        // the window. Tied to `using` so it also happens when a step throws.
        using var shell = _shells.Open(endpoint);

        shell.ReadUntilIdle(SettleFor, StepTimeout);

        report($"Opening the editor: {recipe.EditorCommand}");

        shell.Send(recipe.EditorCommand + ProgressKeys.Enter);
        shell.ReadUntilIdle(SettleFor, StepTimeout);

        foreach (var compile in plan.Qrf)
        {
            report($"  {compile.Statement}");

            shell.Send(ProgressKeys.NewBuffer);
            shell.ReadUntilIdle(SettleFor, StepTimeout);

            shell.Send(compile.Statement + ProgressKeys.Enter);
            shell.ReadUntilIdle(SettleFor, StepTimeout);

            shell.Send(ProgressKeys.Go);
            screens[compile] = shell.ReadUntilIdle(SettleFor, StepTimeout);
        }

        return screens;
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
    private static void VerifySourcesExist(CompilePlan plan, ISftpSession session)
    {
        var missing = plan.Qrf
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
