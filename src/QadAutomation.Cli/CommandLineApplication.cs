using QadAutomation.Cli.Commands;
using QadAutomation.Core.Configuration;
using QadAutomation.Core.Tickets;

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

    public CommandLineApplication(TextWriter output, TextWriter error)
    {
        _output = output;
        _error = error;
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
                return new ValidateConfigCommand(CreateLoader(args), _output).Execute();

            case "tickets":
                return new ListTicketsCommand(CreateTicketReader(args), _output).Execute();

            case "ticket":
                if (string.IsNullOrWhiteSpace(args.Target))
                {
                    _error.WriteLine("Usage: qad ticket <ticket>");
                    return ExitCode.UsageError;
                }

                return new ShowTicketCommand(CreateTicketReader(args), _output).Execute(args.Target);

            default:
                _error.WriteLine($"Unknown command '{args.Command}'.");
                WriteUsage(_error);
                return ExitCode.UsageError;
        }
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
              qad validate            Load the configuration and print a redacted summary
              qad tickets             List ticket folders in the working folder
              qad ticket <ticket>     Show how a ticket folder's files classify as SRC/QRF
              qad help                Show this help

            Options:
              --config <path>         Use a specific configuration file

            Configuration is read from, in order:
              1. --config <path>
              2. the QAD_TOOL_CONFIG environment variable
              3. %APPDATA%\QadAutomationTool\config.json
              4. config.json next to the executable

            No command in this version makes any network connection.
            """);
    }
}
