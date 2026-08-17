using QadAutomation.Cli.Commands;
using QadAutomation.Core.Configuration;
using QadAutomation.Core.Processes;
using QadAutomation.Core.Tickets;
using QadAutomation.Core.Transfer;
using QadAutomation.Core.Vpn;

namespace QadAutomation.Cli;

/// <summary>
/// The composition root: builds the object graph, dispatches to a command, and
/// turns exceptions into exit codes and readable messages.
/// </summary>
/// <remarks>
/// <para>
/// Construction happens here and nowhere else. No class below this point calls
/// <c>new</c> on a collaborator it depends on, which is what keeps them all
/// unit-testable. There is no DI container yet because a graph this small does
/// not need one - and a container would hide, rather than clarify, what depends
/// on what.
/// </para>
/// <para>
/// Output is written to injected <see cref="TextWriter"/>s rather than to
/// <c>Console</c> directly, so the whole application can be driven by a test that
/// asserts on its output.
/// </para>
/// </remarks>
public sealed class CommandLineApplication
{
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly IVpnConnectorFactory _connectors;
    private readonly ISftpSessionFactory _sftp;

    /// <param name="output">Where normal output goes.</param>
    /// <param name="error">Where diagnostics go.</param>
    /// <param name="connectors">
    /// Overridable so an end-to-end test can exercise the VPN commands without a
    /// VPN. Defaults to the real thing, so <c>Program</c> stays a single line.
    /// </param>
    /// <param name="sftp">
    /// Overridable for the same reason, against a server instead of a VPN.
    /// </param>
    public CommandLineApplication(
        TextWriter output,
        TextWriter error,
        IVpnConnectorFactory? connectors = null,
        ISftpSessionFactory? sftp = null)
    {
        _output = output;
        _error = error;
        _connectors = connectors ?? new VpnConnectorFactory(new ProcessRunner());
        _sftp = sftp ?? new SshNetSftpSessionFactory();
    }

    public int Run(string[] args)
    {
        CommandLineArguments parsed;
        try
        {
            parsed = CommandLineParser.Parse(args);
        }
        catch (ArgumentException ex)
        {
            _error.WriteLine(ex.Message);
            WriteUsage(_error);
            return ExitCode.UsageError;
        }

        try
        {
            return Dispatch(parsed);
        }
        catch (ConfigurationException ex)
        {
            // Expected, operator-fixable. A stack trace would only add noise.
            _error.WriteLine(ex.Message);
            return ExitCode.ConfigurationError;
        }
        catch (TicketFolderException ex)
        {
            _error.WriteLine(ex.Message);
            return ExitCode.TicketError;
        }
        catch (VpnException ex)
        {
            _error.WriteLine(ex.Message);
            return ExitCode.VpnError;
        }
        catch (TransferException ex)
        {
            _error.WriteLine(ex.Message);
            return ExitCode.TransferError;
        }
        catch (Exception ex)
        {
            // Anything reaching here is a defect in the tool, so the stack trace
            // is the useful part and is kept.
            _error.WriteLine("Unexpected error:");
            _error.WriteLine(ex);
            return ExitCode.Unexpected;
        }
    }

    private int Dispatch(CommandLineArguments args)
    {
        switch (args.Command.ToLowerInvariant())
        {
            case "help":
                WriteUsage(_output);
                return ExitCode.Ok;

            case "validate":
                return Expect(args, 0, "qad validate")
                    ? new ValidateConfigCommand(CreateLoader(args), _output).Execute()
                    : ExitCode.UsageError;

            case "tickets":
                return Expect(args, 0, "qad tickets")
                    ? new ListTicketsCommand(CreateTicketReader(args), _output).Execute()
                    : ExitCode.UsageError;

            case "ticket":
                return Expect(args, 1, "qad ticket <ticket>")
                    ? new ShowTicketCommand(CreateTicketReader(args), _output).Execute(args.Target!)
                    : ExitCode.UsageError;

            case "vpn":
                return DispatchVpn(args);

            case "check":
                return Expect(args, 2, "qad check <client> <environment>")
                    ? new CheckCommand(CreateLoader(args), _connectors, _sftp, _output, _error)
                        .Execute(args.Target!, args.Argument(1)!)
                    : ExitCode.UsageError;

            case "upload":
                return Expect(args, 3, "qad upload <client> <environment> <ticket> [--dry-run] [--yes] [--no-backup]")
                    ? new UploadCommand(
                            CreateLoader(args),
                            CreateTicketReader(args),
                            _connectors,
                            new FileUploader(_sftp),
                            _output,
                            _error)
                        .Execute(
                            args.Target!,
                            args.Argument(1)!,
                            args.Argument(2)!,
                            new UploadOptions(
                                DryRun: args.HasFlag(CommandLineParser.DryRunFlag),
                                Confirmed: args.HasFlag(CommandLineParser.YesFlag),
                                TakeBackups: !args.HasFlag(CommandLineParser.NoBackupFlag)))
                    : ExitCode.UsageError;

            default:
                _error.WriteLine($"Unknown command '{args.Command}'.");
                WriteUsage(_error);
                return ExitCode.UsageError;
        }
    }

    private int DispatchVpn(CommandLineArguments args)
    {
        const string usage = "qad vpn <status|connect|disconnect> <client>";

        if (!Expect(args, 2, usage))
        {
            return ExitCode.UsageError;
        }

        var command = new VpnCommand(CreateLoader(args), _connectors, _output);
        var clientId = args.Argument(1)!;

        switch (args.Target!.ToLowerInvariant())
        {
            case "status":
                return command.Status(clientId);

            case "connect":
                return command.Connect(clientId);

            case "disconnect":
                return command.Disconnect(clientId);

            default:
                _error.WriteLine($"Unknown vpn action '{args.Target}'.");
                _error.WriteLine($"Usage: {usage}");
                return ExitCode.UsageError;
        }
    }

    /// <summary>
    /// Checks a command was given exactly the arguments it takes, printing the
    /// usage line for that command if not.
    /// </summary>
    /// <remarks>
    /// Both directions matter. Too few is an obvious mistake; too many usually
    /// means a quoting error - <c>qad ticket Ticket #9999555</c> rather than
    /// <c>qad ticket "Ticket #9999555"</c> - and silently ignoring the extra word
    /// would act on the wrong ticket without saying so.
    /// </remarks>
    private bool Expect(CommandLineArguments args, int count, string usage)
    {
        if (args.Arguments.Count == count)
        {
            return true;
        }

        _error.WriteLine(args.Arguments.Count < count
            ? "Not enough arguments."
            : $"Too many arguments. If a value contains a space, quote it.");

        _error.WriteLine($"Usage: {usage}");
        return false;
    }

    private static IConfigurationLoader CreateLoader(CommandLineArguments args) =>
        new JsonConfigurationLoader(new ConfigurationLocator(args.ConfigPath));

    /// <summary>
    /// The ticket reader needs the working folder, which lives in configuration -
    /// so the config is loaded and validated first even for ticket commands.
    /// </summary>
    private static ITicketFolderReader CreateTicketReader(CommandLineArguments args) =>
        new TicketFolderReader(CreateLoader(args).Load().Configuration.WorkingFolder);

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine(
            """
            QAD Compile Automation Tool

            Usage:
              qad validate                  Load the configuration and print a redacted summary
              qad tickets                   List ticket folders in the working folder
              qad ticket <ticket>           Show how a ticket folder's files classify as SRC/QRF
              qad vpn status <client>       Report whether the client's VPN is connected
              qad vpn connect <client>      Bring the client's VPN up and leave it up
              qad vpn disconnect <client>   Take the client's VPN down
              qad check <client> <environment>
                                            Connect and verify the remote paths, read-only
              qad upload <client> <environment> <ticket>
                                            Upload a ticket's SRC/QRF files
              qad help                      Show this help

            Options:
              --config <path>               Use a specific configuration file
              --dry-run                     Show what would happen, change nothing
              --yes                         Confirm an upload to a PRODUCTION environment
              --no-backup                   Overwrite without keeping the previous version

            Examples:
              qad check  pilot TEST                    prove the connection works
              qad upload pilot TEST 9999555 --dry-run  see what would be sent
              qad upload pilot TEST 9999555            send it

            Configuration is read from, in order:
              1. --config <path>
              2. the QAD_TOOL_CONFIG environment variable
              3. config.json in the current directory
              4. config.json next to the executable
              5. %APPDATA%\QadAutomationTool\config.json

            'vpn', 'check' and 'upload' touch the network; 'check' only reads.
            Everything else is local, and 'upload --dry-run' connects to nothing.
            """);
    }
}
