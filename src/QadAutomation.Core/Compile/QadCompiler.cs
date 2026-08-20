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
/// SRC and QRF are compiled by procedures with nothing in common - a batch
/// script over a manifest file, and an interactive editor driven by function
/// keys. Each lives in its own class; this one decides which are needed, opens
/// the single SFTP session they both read timestamps through, and merges the
/// results.
/// </para>
/// <para>
/// <b>SRC runs first.</b> The QRF editor never exits, so the shell it runs in is
/// unusable afterwards. Doing SRC first means a mixed ticket could eventually
/// share one shell; more immediately, it means the half with the more
/// recoverable failure mode runs while the connection is known good.
/// </para>
/// <para>
/// A half of the plan that is empty opens nothing, so the common case - a ticket
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

        if (plan.Src.Count > 0)
        {
            // Non-null by construction: the planner only produces SRC entries
            // when the recipe exists, and skips them with a reason when it does
            // not.
            var recipe = plan.Environment.Compile.Src!;

            programs.AddRange(
                new SrcBatchCompiler(_shells).Compile(plan, recipe, session, endpoint, report));
        }

        if (plan.Qrf.Count > 0)
        {
            var recipe = plan.Environment.Compile.Qrf!;

            programs.AddRange(
                new ProgressEditorCompiler(_shells).Compile(plan, recipe, session, endpoint, report));
        }

        return new CompileOutcome(programs, plan.Skipped);
    }
}
