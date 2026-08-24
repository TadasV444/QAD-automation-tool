using QadAutomation.Core.Configuration;
using QadAutomation.Core.Transfer;

namespace QadAutomation.Core.Compile;

/// <summary>Compiles a ticket's programs on the remote host.</summary>
public interface IProgramCompiler
{
    /// <exception cref="TransferException">
    /// If the host cannot be reached, a source is missing, or a shell dies
    /// mid-run.
    /// </exception>
    CompileOutcome Compile(CompilePlan plan, SshEndpoint endpoint, Action<string>? onProgress = null);
}

/// <summary>
/// Runs whichever compile procedures a ticket needs.
/// </summary>
/// <remarks>
/// <para>
/// Three procedures with nothing in common - a batch script over a manifest
/// file, an interactive editor driven by function keys, and a plain shell
/// command - and which apply depends on the client, not on the kind. Each lives
/// in its own class; this one decides which are needed, opens the single SFTP
/// session they all read timestamps through, and merges the results.
/// </para>
/// <para>
/// <b>The editor runs last.</b> It never exits, so the shell it runs in is
/// unusable afterwards. Everything else therefore happens while the connection
/// is known good.
/// </para>
/// <para>
/// A procedure with nothing to do opens nothing, so the common case - a ticket
/// of one kind - costs exactly one shell.
/// </para>
/// </remarks>
public sealed class QadCompiler : IProgramCompiler
{
    private readonly ISshShellFactory _shells;
    private readonly ISftpSessionFactory _sessions;

    public QadCompiler(ISshShellFactory shells, ISftpSessionFactory sessions)
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

        report($"Connecting to {endpoint.Username}@{endpoint.Host}:{endpoint.Port}...");

        using var session = _sessions.Connect(endpoint);

        report($"Connected. Host key {session.HostKeyFingerprint}");

        var programs = new List<CompiledProgram>();

        // Every recipe below is non-null by construction: the planner only
        // produces an entry when its recipe exists, and skips with a reason
        // when it does not.
        if (plan.Using<PlannedManifestCompile>() is { Count: > 0 } manifest)
        {
            programs.AddRange(new ManifestBatchCompiler(_shells).Compile(
                manifest, plan.Environment.Compile.Src!.Manifest!, session, endpoint, report));
        }

        foreach (var kind in (ProgramKind[])[ProgramKind.Src, ProgramKind.Qrf])
        {
            if (plan.Shell(kind) is not { Count: > 0 } shell)
            {
                continue;
            }

            var recipe = kind == ProgramKind.Src
                ? plan.Environment.Compile.Src!.Shell!
                : plan.Environment.Compile.Qrf!.Shell!;

            programs.AddRange(
                new ShellCommandCompiler(_shells).Compile(shell, recipe, session, endpoint, report));
        }

        // Last: the Progress editor never exits, so the shell it runs in is
        // unusable afterwards.
        if (plan.Using<PlannedEditorCompile>() is { Count: > 0 } editor)
        {
            programs.AddRange(new ProgressEditorCompiler(_shells).Compile(
                editor, plan.Environment.Compile.Qrf!.Editor!, session, endpoint, report));
        }

        return new CompileOutcome(programs, plan.Skipped);
    }
}
