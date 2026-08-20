using QadAutomation.Core.Compile;
using QadAutomation.Core.Configuration;
using QadAutomation.Core.Tickets;
using QadAutomation.Core.Vpn;

namespace QadAutomation.Cli.Commands;

/// <summary>
/// <c>qad compile &lt;client&gt; &lt;environment&gt; &lt;ticket&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <see cref="UploadCommand"/> - plan, print, confirm,
/// connect, act - because the two will eventually be halves of one
/// <c>qad deploy</c>, and a step that behaved differently would be the one an
/// operator got wrong.
/// </para>
/// <para>
/// It exits non-zero when anything was skipped, not only when a compile failed.
/// A ticket whose SRC half was never built, reported as success, is precisely
/// the mistake this tool exists to make impossible.
/// </para>
/// </remarks>
public sealed class CompileCommand
{
    private readonly IConfigurationLoader _loader;
    private readonly ITicketFolderReader _tickets;
    private readonly IVpnConnectorFactory _connectors;
    private readonly IProgramCompiler _compiler;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public CompileCommand(
        IConfigurationLoader loader,
        ITicketFolderReader tickets,
        IVpnConnectorFactory connectors,
        IProgramCompiler compiler,
        TextWriter output,
        TextWriter error)
    {
        _loader = loader;
        _tickets = tickets;
        _connectors = connectors;
        _compiler = compiler;
        _output = output;
        _error = error;
    }

    public int Execute(string clientId, string environmentName, string ticketName, CompileOptions options)
    {
        var configuration = _loader.Load().Configuration;
        var client = configuration.RequireClient(clientId);
        var environment = client.RequireEnvironment(environmentName);

        var ticket = _tickets.Read(ticketName);
        var plan = CompilePlan.Create(ticket, environment, client.Id);

        WritePlan(client, plan);

        if (plan.IsEmpty)
        {
            // Skips have already been printed. Nothing to compile is only "Ok"
            // when there was also nothing to skip.
            _output.WriteLine("Nothing to compile.");
            return plan.Skipped.Count == 0 ? ExitCode.Ok : ExitCode.UsageError;
        }

        if (options.DryRun)
        {
            _output.WriteLine("Dry run - nothing was compiled.");
            return ExitCode.Ok;
        }

        if (plan.IsProduction && !options.Confirmed)
        {
            _error.WriteLine(
                $"'{environment.Name}' is a PRODUCTION environment. Re-run with --yes to go ahead, " +
                "or with --dry-run to see the plan again.");
            return ExitCode.UsageError;
        }

        var connector = _connectors.Create(client.Vpn);

        using var session = connector.Connect(client.Vpn);

        if (session.OpenedByTool)
        {
            _output.WriteLine($"VPN '{session.ConnectionName}' connected.");
        }

        var outcome = _compiler.Compile(plan, environment.Ssh, line => _output.WriteLine(line));

        return WriteOutcome(outcome);
    }

    private void WritePlan(ClientProfile client, CompilePlan plan)
    {
        _output.WriteLine($"Ticket      : {plan.TicketName}");
        _output.WriteLine($"Client      : {client.DisplayName} [{client.Id}]");

        var marker = plan.IsProduction ? "   ** PRODUCTION **" : string.Empty;
        _output.WriteLine($"Environment : {plan.Environment.Name}{marker}");
        _output.WriteLine($"Server      : {plan.Environment.Ssh.Username}@{plan.Environment.Ssh.Host}:{plan.Environment.Ssh.Port}");
        _output.WriteLine();

        if (plan.Compiles.Count > 0)
        {
            _output.WriteLine($"{plan.Compiles.Count} program(s) to compile:");
            _output.WriteLine();

            foreach (var compile in plan.Compiles)
            {
                _output.WriteLine($"  [{compile.Kind.ToString().ToUpperInvariant()}] {compile.File.FileName}");

                // The statement is shown in full because it is what will
                // actually be typed. A wrong path here is the difference
                // between compiling and compiling the wrong thing.
                _output.WriteLine($"      {compile.Statement}");
                _output.WriteLine($"      -> {compile.RemoteResult}");
            }

            _output.WriteLine();
        }

        WriteSkips(plan.Skipped);
    }

    private void WriteSkips(IReadOnlyList<SkippedProgram> skipped)
    {
        if (skipped.Count == 0)
        {
            return;
        }

        _output.WriteLine($"{skipped.Count} program(s) will NOT be compiled:");

        foreach (var skip in skipped)
        {
            _output.WriteLine($"  [{skip.File.Kind.ToString().ToUpperInvariant()}] {skip.File.FileName}");
            _output.WriteLine($"      {skip.Reason}");
        }

        _output.WriteLine();
    }

    private int WriteOutcome(CompileOutcome outcome)
    {
        _output.WriteLine();
        _output.WriteLine($"Done - {outcome.CompiledCount} compiled, {outcome.FailedCount} failed.");

        foreach (var failure in outcome.Failures)
        {
            _error.WriteLine();
            _error.WriteLine($"{failure.Planned.File.FileName} did not compile.");
            _error.WriteLine($"  {failure.Planned.RemoteResult} was not updated.");

            var screen = Readable(failure.Screen);

            if (screen.Count > 0)
            {
                _error.WriteLine("  The editor showed:");

                foreach (var line in screen)
                {
                    _error.WriteLine($"    {line}");
                }
            }
        }

        if (outcome.FailedCount > 0)
        {
            return ExitCode.CompileError;
        }

        // Skips were listed before the run; this is the reminder at the end,
        // where the operator is actually deciding whether they are finished.
        return outcome.Skipped.Count == 0 ? ExitCode.Ok : ExitCode.UsageError;
    }

    /// <summary>
    /// Turns a captured terminal screen into something worth printing.
    /// </summary>
    /// <remarks>
    /// The raw capture is full of cursor-positioning escape sequences and blank
    /// padding rows. Stripping them is presentation only - nothing here has any
    /// say in whether the compile succeeded, so an imperfect filter costs
    /// readability and never correctness.
    /// </remarks>
    private static IReadOnlyList<string> Readable(string screen)
    {
        if (string.IsNullOrWhiteSpace(screen))
        {
            return [];
        }

        var stripped = new string([.. screen.Where(c => !char.IsControl(c) || c is '\n' or '\r')]);

        return [.. stripped
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(20)];
    }
}

/// <summary>Switches affecting how a compile runs.</summary>
/// <param name="DryRun">Print the plan and stop.</param>
/// <param name="Confirmed">The operator passed <c>--yes</c>.</param>
public sealed record CompileOptions(bool DryRun, bool Confirmed);
