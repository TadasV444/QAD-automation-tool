using QadAutomation.Core.Compile;
using QadAutomation.Core.Configuration;
using QadAutomation.Core.Tickets;
using QadAutomation.Core.Transfer;
using QadAutomation.Core.Vpn;

namespace QadAutomation.Cli.Commands;

/// <summary>
/// <c>qad deploy &lt;client&gt; &lt;environment&gt; &lt;ticket&gt;</c> - upload, then compile.
/// </summary>
/// <remarks>
/// <para>
/// The command the tool exists for: what used to be a VPN dial, a FileZilla
/// session and a PuTTY session, in one line.
/// </para>
/// <para>
/// <b>Both plans are built and printed before anything connects.</b> That is the
/// point of doing this as one command rather than two: the operator sees what
/// will be uploaded <i>and</i> what will be compiled, and can decide once. Two
/// separate commands mean the compile is only described after the upload has
/// already happened, when the decision has stopped being reversible.
/// </para>
/// <para>
/// <b>One VPN session for both halves.</b> Running the two commands in sequence
/// dials and drops the VPN twice, which is slow and gives the connection two
/// chances to fail instead of one.
/// </para>
/// <para>
/// <b>A failed upload stops the run.</b> Compiling after a partial upload would
/// build some new programs and some old ones, with nothing to say which.
/// </para>
/// </remarks>
public sealed class DeployCommand
{
    private readonly IConfigurationLoader _loader;
    private readonly ITicketFolderReader _tickets;
    private readonly IVpnConnectorFactory _connectors;
    private readonly IFileUploader _uploader;
    private readonly IProgramCompiler _compiler;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public DeployCommand(
        IConfigurationLoader loader,
        ITicketFolderReader tickets,
        IVpnConnectorFactory connectors,
        IFileUploader uploader,
        IProgramCompiler compiler,
        TextWriter output,
        TextWriter error)
    {
        _loader = loader;
        _tickets = tickets;
        _connectors = connectors;
        _uploader = uploader;
        _compiler = compiler;
        _output = output;
        _error = error;
    }

    public int Execute(string clientId, string environmentName, string ticketName, DeployOptions options)
    {
        var configuration = _loader.Load().Configuration;
        var client = configuration.RequireClient(clientId);
        var environment = client.RequireEnvironment(environmentName);

        var ticket = _tickets.Read(ticketName);

        var uploadPlan = UploadPlan.Create(ticket, environment, client.Id);
        var compilePlan = CompilePlan.Create(ticket, environment, client.Id);

        PlanDisplay.WriteHeader(_output, client, ticket.Name, environment);
        PlanDisplay.WriteUploadPlan(_output, uploadPlan);
        PlanDisplay.WriteCompilePlan(_output, compilePlan);

        if (uploadPlan.IsEmpty)
        {
            _output.WriteLine("Nothing to deploy.");
            return ExitCode.Ok;
        }

        if (options.DryRun)
        {
            _output.WriteLine("Dry run - nothing was uploaded or compiled.");
            return ExitCode.Ok;
        }

        if (environment.IsProduction && !options.Confirmed)
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

        // If this throws, the exception carries the exit code and the compile
        // never starts - which is the behaviour we want and the reason there is
        // no try/catch here.
        var uploaded = _uploader.Upload(
            uploadPlan,
            environment.Ssh,
            takeBackups: options.TakeBackups,
            onProgress: _output.WriteLine);

        UploadCommand.WriteOutcome(_output, uploaded, client.Id, environment.Name, ticket.Name);

        if (compilePlan.IsEmpty)
        {
            _output.WriteLine();
            _output.WriteLine("Nothing to compile.");

            // The upload worked. Whether that is the whole job depends on
            // whether anything was skipped, which was printed above.
            return compilePlan.Skipped.Count == 0 ? ExitCode.Ok : ExitCode.UsageError;
        }

        _output.WriteLine();

        var compiled = _compiler.Compile(compilePlan, environment.Ssh, _output.WriteLine);

        return CompileCommand.WriteOutcome(_output, _error, compiled);
    }
}

/// <summary>Switches affecting how a deploy runs.</summary>
/// <param name="DryRun">Print both plans and stop.</param>
/// <param name="Confirmed">The operator passed <c>--yes</c>.</param>
/// <param name="TakeBackups">Keep a local copy of anything replaced.</param>
public sealed record DeployOptions(bool DryRun, bool Confirmed, bool TakeBackups);
