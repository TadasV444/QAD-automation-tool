using System.Globalization;
using QadAutomation.Core.Configuration;
using QadAutomation.Core.Tickets;
using QadAutomation.Core.Transfer;

namespace QadAutomation.Core.Compile;

/// <summary>
/// Compiles by running one ordinary shell command.
/// </summary>
/// <remarks>
/// <para>
/// The site's own build script finds the changed programs itself, so unlike the
/// other two procedures there is nothing to list and nothing to type per file.
/// One command builds whatever the ticket put on the server.
/// </para>
/// <para>
/// <b>This is the only procedure with an exit code to read.</b> The Progress
/// editor has none, and the manifest script's is swallowed by its dialog. A
/// plain command reports one, and it is captured by echoing a marker
/// immediately afterwards - the shell is a raw stream, so there is no other way
/// to ask.
/// </para>
/// <para>
/// An exit code is still only a claim, though: a script that always exits zero
/// would report every failure as a success, which is the one direction of error
/// this tool must not make. So when the site tells us where compiled output
/// lands, those timestamps decide and the exit code becomes supporting evidence.
/// Only when the location is unknown does the code decide alone.
/// </para>
/// </remarks>
internal sealed class ShellCommandCompiler
{
    /// <summary>
    /// How long the login banner must be quiet before the shell is ready.
    /// </summary>
    /// <remarks>
    /// Used only before any command runs, where a pause genuinely does mean
    /// nothing more is coming. The command itself is waited on by its marker,
    /// not by silence.
    /// </remarks>
    private static readonly TimeSpan SettleFor = TimeSpan.FromSeconds(2);

    /// <summary>Upper bound on one command.</summary>
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(10);

    private readonly ISshShellFactory _shells;

    public ShellCommandCompiler(ISshShellFactory shells) => _shells = shells;

    public IReadOnlyList<CompiledProgram> Compile(
        IReadOnlyList<PlannedShellCompile> planned,
        ShellCompileSettings recipe,
        ISftpSession session,
        SshEndpoint endpoint,
        Action<string> report)
    {
        VerifySourcesExist(planned, session);

        var before = planned.ToDictionary(
            compile => compile,
            compile => compile.RemoteResult is null ? null : session.LastWriteTime(compile.RemoteResult));

        var (screen, exitCode) = Run(recipe, endpoint, report);

        return [.. planned.Select(compile =>
        {
            var result = Verdict(compile, before[compile], exitCode, session);

            report($"  {(result == CompileResult.Compiled ? "compiled" : "FAILED  ")} {compile.File.FileName}");

            return new CompiledProgram(compile, result, screen);
        })];
    }

    /// <summary>
    /// Decides one program's fate from whatever evidence the site affords.
    /// </summary>
    /// <remarks>
    /// The artefact wins wherever there is one. A build script that reports
    /// success while writing nothing is a real possibility and the failure it
    /// causes is silent; the reverse - a moved file with a non-zero code - would
    /// at worst be reported conservatively.
    /// </remarks>
    private static CompileResult Verdict(
        PlannedShellCompile compile, DateTimeOffset? before, int? exitCode, ISftpSession session)
    {
        if (compile.RemoteResult is not { } result)
        {
            return exitCode is 0 ? CompileResult.Compiled : CompileResult.Failed;
        }

        var after = session.LastWriteTime(result);

        return ProgressEditorCompiler.Moved(before, after) ? CompileResult.Compiled : CompileResult.Failed;
    }

    private (string Screen, int? ExitCode) Run(
        ShellCompileSettings recipe, SshEndpoint endpoint, Action<string> report)
    {
        using var shell = _shells.Open(endpoint);

        // The login banner. Idle is the right test here - there is no command
        // running, so a pause really does mean the shell is ready.
        shell.ReadUntilIdle(SettleFor, CommandTimeout);

        shell.Send($"cd {recipe.WorkingDirectory}" + ProgressKeys.Enter);
        shell.ReadUntilIdle(SettleFor, CommandTimeout);

        report($"  {recipe.Command}");

        // Command and status echo as one line. The echo is both the answer and
        // the signal that the command has finished - waiting for a pause
        // instead would give up in the middle of a slow build.
        shell.Send(ShellProtocol.WithExitCode(recipe.Command) + ProgressKeys.Enter);

        var screen = shell.ReadUntil(ShellProtocol.CompletedExitCode, CommandTimeout);

        return (screen, ExitCodeIn(screen));
    }

    /// <summary>
    /// Finds the echoed exit code, or <c>null</c> if it never arrived.
    /// </summary>
    /// <remarks>
    /// Null means the command was still running when time ran out. Reported as
    /// a failure, because an unanswered question is not a yes.
    /// </remarks>
    private static int? ExitCodeIn(string output) =>
        ShellProtocol.CompletedExitCode.Match(output) is { Success: true } match
            ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
            : null;

    private static void VerifySourcesExist(IReadOnlyList<PlannedShellCompile> planned, ISftpSession session)
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
