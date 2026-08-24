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
    /// How long output must stop before the command is considered finished.
    /// </summary>
    /// <remarks>
    /// The marker echo is what actually ends the wait; this only bounds the gap
    /// between lines of a build that is still talking.
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

        shell.ReadUntilIdle(SettleFor, CommandTimeout);

        shell.Send($"cd {recipe.WorkingDirectory}" + ProgressKeys.Enter);
        shell.ReadUntilIdle(SettleFor, CommandTimeout);

        report($"  {recipe.Command}");

        shell.Send(recipe.Command + ProgressKeys.Enter);

        var screen = shell.ReadUntilIdle(SettleFor, CommandTimeout);

        // Echoed as its own command so the value is the previous command's,
        // captured before anything else can overwrite $?.
        shell.Send(ShellProtocol.EchoExitCode + ProgressKeys.Enter);

        var tail = shell.ReadUntilIdle(SettleFor, CommandTimeout);

        return (screen, ExitCodeIn(tail));
    }

    /// <summary>
    /// Finds the echoed exit code, or <c>null</c> if the marker never appeared.
    /// </summary>
    /// <remarks>
    /// The command line itself echoes back before its output, so the marker
    /// occurs twice - once in the echoed command, where it is followed by
    /// <c>$?</c>, and once in the result. Taking the last occurrence that parses
    /// as a number is what tells them apart.
    /// </remarks>
    private static int? ExitCodeIn(string output)
    {
        int? found = null;

        var index = output.IndexOf(ShellProtocol.ExitMarker, StringComparison.Ordinal);

        while (index >= 0)
        {
            var digits = new string([.. output[(index + ShellProtocol.ExitMarker.Length)..].TakeWhile(char.IsDigit)]);

            if (digits.Length > 0)
            {
                found = int.Parse(digits, CultureInfo.InvariantCulture);
            }

            index = output.IndexOf(ShellProtocol.ExitMarker, index + ShellProtocol.ExitMarker.Length, StringComparison.Ordinal);
        }

        return found;
    }

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
