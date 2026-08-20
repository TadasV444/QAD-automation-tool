using QadAutomation.Core.Configuration;
using QadAutomation.Core.Transfer;

namespace QadAutomation.Core.Compile;

/// <summary>Compiles a ticket's programs on the remote host.</summary>
public interface IProgramCompiler
{
    /// <exception cref="TransferException">
    /// If the host cannot be reached or the shell dies mid-run.
    /// </exception>
    CompileOutcome Compile(CompilePlan plan, SshEndpoint endpoint, Action<string>? onProgress = null);
}

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
/// <para>
/// Two connections are opened: a shell to type into, and an SFTP session to read
/// timestamps. They cannot be the same channel, and the alternative - shelling
/// out to <c>ls</c> and parsing it - would put the verdict back on screen
/// scraping, which is precisely what this design avoids.
/// </para>
/// </remarks>
public sealed class ProgressEditorCompiler : IProgramCompiler
{
    /// <summary>
    /// How long output must stop before the screen is considered settled.
    /// </summary>
    /// <remarks>
    /// The operator reports compiles finishing "very quickly", so this is tuned
    /// for a screen redraw rather than for the compile itself. Too short and a
    /// statement is typed into an editor that has not finished drawing; the cost
    /// of too long is only wasted seconds.
    /// </remarks>
    private static readonly TimeSpan SettleFor = TimeSpan.FromMilliseconds(750);

    /// <summary>Upper bound on any single wait, so a hung editor cannot hang the tool.</summary>
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

    private readonly ISshShellFactory _shells;
    private readonly ISftpSessionFactory _sessions;

    public ProgressEditorCompiler(ISshShellFactory shells, ISftpSessionFactory sessions)
    {
        _shells = shells;
        _sessions = sessions;
    }

    /// <inheritdoc />
    public CompileOutcome Compile(CompilePlan plan, SshEndpoint endpoint, Action<string>? onProgress = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(endpoint);

        var report = onProgress ?? (_ => { });

        if (plan.IsEmpty)
        {
            return new CompileOutcome([], plan.Skipped);
        }

        var recipe = plan.Environment.Compile.Qrf
            ?? throw new ConfigurationException(
                $"'{plan.Environment.Name}' has no 'compile.qrf' block, so nothing can be compiled.");

        report($"Connecting to {endpoint.Username}@{endpoint.Host}:{endpoint.Port}...");

        using var session = _sessions.Connect(endpoint);

        report($"Connected. Host key {session.HostKeyFingerprint}");

        VerifySourcesExist(plan, session);

        // Read before anything is typed. Asking afterwards could only say what
        // the timestamp is, not whether it moved.
        var before = plan.Compiles.ToDictionary(
            compile => compile,
            compile => session.LastWriteTime(compile.RemoteResult));

        var screens = RunEditor(plan, recipe, endpoint, report);

        var programs = plan.Compiles
            .Select(compile =>
            {
                var after = session.LastWriteTime(compile.RemoteResult);
                var result = Moved(before[compile], after) ? CompileResult.Compiled : CompileResult.Failed;

                report($"  {(result == CompileResult.Compiled ? "compiled" : "FAILED  ")} {compile.File.FileName}");

                return new CompiledProgram(compile, result, screens.GetValueOrDefault(compile, string.Empty));
            })
            .ToList();

        return new CompileOutcome(programs, plan.Skipped);
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
    private Dictionary<PlannedCompile, string> RunEditor(
        CompilePlan plan,
        QrfCompileSettings recipe,
        SshEndpoint endpoint,
        Action<string> report)
    {
        var screens = new Dictionary<PlannedCompile, string>();

        // Disposing the shell closes the channel, which is how this editor is
        // left - it has no exit key here, and the manual procedure is to close
        // the window. Tied to `using` so it also happens when a step throws.
        using var shell = _shells.Open(endpoint);

        shell.ReadUntilIdle(SettleFor, StepTimeout);

        report($"Opening the editor: {recipe.EditorCommand}");

        shell.Send(recipe.EditorCommand + ProgressKeys.Enter);
        shell.ReadUntilIdle(SettleFor, StepTimeout);

        foreach (var compile in plan.Compiles)
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
    /// A <c>.r</c> that did not exist and now does counts, which is the ordinary
    /// case for a brand new report. Everything else needs the timestamp to have
    /// advanced: Progress leaves the old <c>.r</c> exactly as it was when a
    /// compile fails, so "unchanged" and "failed" are the same observation.
    /// </remarks>
    private static bool Moved(DateTimeOffset? before, DateTimeOffset? after) =>
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
        var missing = plan.Compiles
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
