using QadAutomation.Cli.Commands;
using QadAutomation.Core.Compile;
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
    private readonly ISshShellFactory _shells;
    private readonly TextReader _input;
    private readonly bool _ownsConsole;

    /// <param name="output">Where normal output goes.</param>
    /// <param name="error">Where diagnostics go.</param>
    /// <param name="connectors">
    /// Overridable so an end-to-end test can exercise the VPN commands without a
    /// VPN. Defaults to the real thing, so <c>Program</c> stays a single line.
    /// </param>
    /// <param name="sftp">
    /// Overridable for the same reason, against a server instead of a VPN.
    /// </param>
    /// <param name="shells">
    /// Overridable so the compile step can be driven without a Progress editor
    /// on the other end. Separate from <paramref name="sftp"/> because they are
    /// genuinely two connections: one to type into, one to read timestamps from.
    /// </param>
    /// <param name="input">
    /// Where the guided flow reads its answers. Overridable for the same reason
    /// the writers are: a menu whose answers can be supplied by a test is a menu
    /// that can be tested at all.
    /// </param>
    public CommandLineApplication(
        TextWriter output,
        TextWriter error,
        IVpnConnectorFactory? connectors = null,
        ISftpSessionFactory? sftp = null,
        ISshShellFactory? shells = null,
        TextReader? input = null)
    {
        _output = output;
        _error = error;
        _connectors = connectors ?? new VpnConnectorFactory(new ProcessRunner());
        _sftp = sftp ?? new SshNetSftpSessionFactory();
        _shells = shells ?? new SshNetShellFactory();
        _input = input ?? Console.In;

        // A double-clicked shortcut gets its own console window, which Windows
        // closes the moment the process exits - taking the result with it. So
        // the guided flow waits for a keypress before it ends.
        //
        // Only when nobody supplied the input and nothing is piped in: a test
        // driving the flow from a string, or a script feeding it answers, must
        // not be left waiting for a person who is not there.
        _ownsConsole = input is null && !Console.IsInputRedirected;
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
        catch (Exception ex) when (ExitCode.IsExpected(ex))
        {
            // Operator-fixable. A stack trace would only add noise.
            _error.WriteLine(ex.Message);
            HoldConsoleOpen(parsed);
            return ExitCode.For(ex);
        }
        catch (Exception ex)
        {
            // Anything reaching here is a defect in the tool, so the stack trace
            // is the useful part and is kept.
            _error.WriteLine("Unexpected error:");
            _error.WriteLine(ex);
            HoldConsoleOpen(parsed);
            return ExitCode.Unexpected;
        }
    }

    /// <summary>
    /// Waits for a keypress, but only after a guided flow that ended in an
    /// error, and only when this process owns the window it printed to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A guided flow that ran to completion has already held the window open
    /// with its own "run again?" question, so a second prompt would be one
    /// keypress too many. This covers the case that question is never reached.
    /// </para>
    /// <para>
    /// Never after a command-line verb. Someone who typed <c>qad upload ...</c>
    /// has a shell to return to and would find the pause an obstruction; someone
    /// who double-clicked has nowhere for the output to go.
    /// </para>
    /// </remarks>
    private void HoldConsoleOpen(CommandLineArguments args)
    {
        if (!_ownsConsole ||
            !string.Equals(args.Command, CommandLineParser.MenuCommand, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _output.WriteLine();
        _output.Write("Press Enter to close...");
        _input.ReadLine();
    }

    private int Dispatch(CommandLineArguments args)
    {
        switch (args.Command.ToLowerInvariant())
        {
            case "help":
                WriteUsage(_output);
                return ExitCode.Ok;

            case CommandLineParser.MenuCommand:
                return Expect(args, 0, "qad menu")
                    ? new LauncherCommand(
                            CreateLoader(args),
                            CreateTicketReader(args),
                            CreateDeploy(args),
                            _input,
                            _output,
                            _error)
                        .Execute()
                    : ExitCode.UsageError;

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

            case "deploy":
                return Expect(args, 3, "qad deploy <client> <environment> <ticket> [--dry-run] [--yes] [--no-backup]")
                    ? CreateDeploy(args)
                        .Execute(
                            args.Target!,
                            args.Argument(1)!,
                            args.Argument(2)!,
                            new DeployOptions(
                                DryRun: args.HasFlag(CommandLineParser.DryRunFlag),
                                Confirmed: args.HasFlag(CommandLineParser.YesFlag),
                                TakeBackups: !args.HasFlag(CommandLineParser.NoBackupFlag)))
                    : ExitCode.UsageError;

            case "compile":
                return Expect(args, 3, "qad compile <client> <environment> <ticket> [--dry-run] [--yes]")
                    ? new CompileCommand(
                            CreateLoader(args),
                            CreateTicketReader(args),
                            _connectors,
                            new QadCompiler(_shells, _sftp),
                            _output,
                            _error)
                        .Execute(
                            args.Target!,
                            args.Argument(1)!,
                            args.Argument(2)!,
                            new CompileOptions(
                                DryRun: args.HasFlag(CommandLineParser.DryRunFlag),
                                Confirmed: args.HasFlag(CommandLineParser.YesFlag)))
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

    /// <summary>
    /// The upload-then-compile command, built the same way for both the
    /// command line and the guided flow - so neither can quietly do something
    /// the other does not.
    /// </summary>
    private DeployCommand CreateDeploy(CommandLineArguments args) =>
        new(
            CreateLoader(args),
            CreateTicketReader(args),
            _connectors,
            new FileUploader(_sftp),
            new QadCompiler(_shells, _sftp),
            _output,
            _error);

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
              qad                           Guided: pick a client, environment and ticket
              qad menu                      The same thing, named
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
              qad compile <client> <environment> <ticket>
                                            Compile a ticket's QRF reports
              qad deploy <client> <environment> <ticket>
                                            Upload then compile, in one VPN session
              qad help                      Show this help

            Options:
              --config <path>               Use a specific configuration file
              --dry-run                     Show what would happen, change nothing
              --yes                         Confirm an upload to a PRODUCTION environment
              --no-backup                   Overwrite without keeping the previous version

            Examples:
              qad check   pilot TEST                    prove the connection works
              qad upload  pilot TEST 9999555 --dry-run  see what would be sent
              qad upload  pilot TEST 9999555            send it
              qad compile pilot TEST 9999555            build it on the server
              qad deploy  pilot TEST 9999555            both, in one go

            Configuration is read from, in order:
              1. --config <path>
              2. the QAD_TOOL_CONFIG environment variable
              3. config.json in the current directory
              4. config.json next to the executable
              5. %APPDATA%\QadAutomationTool\config.json

            'vpn', 'check', 'upload', 'compile' and 'deploy' touch the network;
            'check' only reads. Everything else is local, and '--dry-run'
            connects to nothing.
            """);
    }
}
