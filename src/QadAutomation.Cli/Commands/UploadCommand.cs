using QadAutomation.Core.Configuration;
using QadAutomation.Core.Tickets;
using QadAutomation.Core.Transfer;
using QadAutomation.Core.Vpn;

namespace QadAutomation.Cli.Commands;

/// <summary>
/// <c>qad upload &lt;client&gt; &lt;environment&gt; &lt;ticket&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// The first command that changes something on a client's server, and the shape
/// the eventual <c>qad deploy</c> will take - it already does connect, resolve,
/// upload and disconnect in one run.
/// </para>
/// <para>
/// Three safety properties, in the order they matter:
/// </para>
/// <list type="bullet">
///   <item><b>The plan is built and printed before anything connects.</b> With
///         <c>--dry-run</c> that is all that happens, and because the printed
///         plan is the same object the upload would consume, it cannot
///         misrepresent what would occur.</item>
///   <item><b>Production requires <c>--yes</c>.</b> A flag rather than a typed
///         prompt because this must stay usable from a shortcut; refusing by
///         default keeps the accident impossible while leaving the deliberate
///         act one word away.</item>
///   <item><b>The VPN is restored to how it was found.</b> The session
///         disconnects on the way out only if this run opened it - including
///         when the upload throws.</item>
/// </list>
/// </remarks>
public sealed class UploadCommand
{
    private readonly IConfigurationLoader _loader;
    private readonly ITicketFolderReader _tickets;
    private readonly IVpnConnectorFactory _connectors;
    private readonly IFileUploader _uploader;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public UploadCommand(
        IConfigurationLoader loader,
        ITicketFolderReader tickets,
        IVpnConnectorFactory connectors,
        IFileUploader uploader,
        TextWriter output,
        TextWriter error)
    {
        _loader = loader;
        _tickets = tickets;
        _connectors = connectors;
        _uploader = uploader;
        _output = output;
        _error = error;
    }

    public int Execute(string clientId, string environmentName, string ticketName, UploadOptions options)
    {
        var configuration = _loader.Load().Configuration;
        var client = configuration.RequireClient(clientId);
        var environment = client.RequireEnvironment(environmentName);

        var ticket = _tickets.Read(ticketName);
        var plan = UploadPlan.Create(ticket, environment, client.Id);

        WritePlan(client, plan);

        if (plan.IsEmpty)
        {
            // Not an error. An empty ticket folder is a normal thing to find and
            // the operator has just been told so.
            _output.WriteLine("Nothing to upload.");
            return ExitCode.Ok;
        }

        if (options.DryRun)
        {
            _output.WriteLine("Dry run - nothing was uploaded.");
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

        // Restores whatever VPN state it found, on every path out of this block.
        using var session = connector.Connect(client.Vpn);

        if (session.OpenedByTool)
        {
            _output.WriteLine($"VPN '{session.ConnectionName}' connected.");
        }

        var outcome = _uploader.Upload(
            plan,
            environment.Ssh,
            takeBackups: options.TakeBackups,
            onProgress: line => _output.WriteLine(line));

        WriteOutcome(outcome, client.Id, environment.Name, plan.TicketName);

        return ExitCode.Ok;
    }

    private void WritePlan(ClientProfile client, UploadPlan plan)
    {
        _output.WriteLine($"Ticket      : {plan.TicketName}");
        _output.WriteLine($"Client      : {client.DisplayName} [{client.Id}]");

        var marker = plan.IsProduction ? "   ** PRODUCTION **" : string.Empty;
        _output.WriteLine($"Environment : {plan.Environment.Name}{marker}");
        _output.WriteLine($"Server      : {plan.Environment.Ssh.Username}@{plan.Environment.Ssh.Host}:{plan.Environment.Ssh.Port}");
        _output.WriteLine();

        if (plan.IsEmpty)
        {
            _output.WriteLine("No SRC or QRF files found in this ticket folder.");
            return;
        }

        _output.WriteLine($"{plan.Uploads.Count} file(s) to upload:");

        // Grouped by destination so a file about to go somewhere unexpected is
        // obvious at a glance, rather than buried in a flat list.
        foreach (var group in plan.Uploads.GroupBy(u => u.RemoteDirectory).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            _output.WriteLine();
            _output.WriteLine($"  {group.Key}/");

            foreach (var upload in group)
            {
                _output.WriteLine($"    [{upload.Kind.ToString().ToUpperInvariant()}] {upload.File.FileName}");
            }
        }

        _output.WriteLine();
    }

    private void WriteOutcome(UploadOutcome outcome, string clientId, string environmentName, string ticketName)
    {
        _output.WriteLine();
        _output.WriteLine($"Done - {outcome.CreatedCount} created, {outcome.ReplacedCount} replaced.");

        if (outcome.WithBackups.Count == 0)
        {
            return;
        }

        // Printed as ready-to-paste commands: if the deploy turns out to be
        // wrong, the operator needs the undo in the next thirty seconds, not
        // after reconstructing filenames from memory.
        _output.WriteLine();
        _output.WriteLine($"Previous versions saved to {outcome.BackupFolder}");
        _output.WriteLine();
        _output.WriteLine("To undo, copy them back over the ticket folder and upload again:");

        foreach (var file in outcome.WithBackups)
        {
            _output.WriteLine($"  copy /Y \"{file.LocalBackupPath}\" \"{file.Planned.File.LocalPath}\"");
        }

        // The restore is a re-upload rather than a server-side move, so the
        // second half of the undo is this tool. Spelled out because the first
        // half looks complete on its own.
        _output.WriteLine($"  qad upload {clientId} {environmentName} {Quote(ticketName)} --no-backup");
    }

    /// <summary>
    /// Quotes a ticket name for the printed command line if it needs it.
    /// </summary>
    /// <remarks>
    /// Ticket folders are named like <c>Ticket #9999555</c> - a space and a
    /// character the shell may treat as a comment. An undo line that cannot be
    /// pasted is worse than none, because it is trusted.
    /// </remarks>
    private static string Quote(string value) =>
        value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;
}

/// <summary>Switches affecting how an upload runs.</summary>
/// <param name="DryRun">Print the plan and stop.</param>
/// <param name="Confirmed">The operator passed <c>--yes</c>.</param>
/// <param name="TakeBackups">
/// Download each file about to be replaced into the ticket's <c>_backup</c>
/// folder first. Nothing is written to the server either way.
/// </param>
public sealed record UploadOptions(bool DryRun, bool Confirmed, bool TakeBackups);
