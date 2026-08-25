using QadAutomation.Core.Configuration;
using QadAutomation.Core.Tickets;

namespace QadAutomation.Cli.Commands;

/// <summary>
/// The guided flow: pick a client, an environment and a ticket, then deploy.
/// </summary>
/// <remarks>
/// <para>
/// For the operator who does not want to remember four client ids and the exact
/// spelling of nine environments. It runs when <c>qad</c> is given nothing to
/// do, which is what a double-clicked shortcut does.
/// </para>
/// <para>
/// <b>It decides nothing.</b> Every question it asks produces an argument that
/// could have been typed, and the deploy it runs is the same
/// <see cref="DeployCommand"/> the command line reaches. Anything it did
/// differently would be behaviour that only exists down one path, and only the
/// other path has tests worth the name.
/// </para>
/// <para>
/// The plan is printed and confirmed before anything connects, by running the
/// deploy twice: once as a dry run, then for real. That costs a second read of
/// a local folder and buys the operator a look at what they are about to do -
/// which the command line gets by typing <c>--dry-run</c> first, and which
/// nobody would do here.
/// </para>
/// </remarks>
public sealed class LauncherCommand
{
    private readonly IConfigurationLoader _loader;
    private readonly ITicketFolderReader _tickets;
    private readonly DeployCommand _deploy;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public LauncherCommand(
        IConfigurationLoader loader,
        ITicketFolderReader tickets,
        DeployCommand deploy,
        TextReader input,
        TextWriter output,
        TextWriter error)
    {
        _loader = loader;
        _tickets = tickets;
        _deploy = deploy;
        _input = input;
        _output = output;
        _error = error;
    }

    public int Execute()
    {
        var configuration = _loader.Load().Configuration;

        _output.WriteLine("QAD Compile Automation Tool");
        _output.WriteLine();

        // The id is shown beside the name because it is what the operator types
        // on the command line, and this list is where they learn it. Padded so
        // the two read as columns rather than as a ragged sentence.
        var width = configuration.Clients.Max(client => client.DisplayName.Length);

        if (Choose(
                "Client",
                configuration.Clients,
                client => $"{client.DisplayName.PadRight(width)}  [{client.Id}]")
            is not { } chosenClient)
        {
            return Cancelled();
        }

        if (Choose(
                $"Environment for {chosenClient.DisplayName}",
                chosenClient.Environments,
                Describe)
            is not { } environment)
        {
            return Cancelled();
        }

        var tickets = _tickets.ListTickets();

        if (tickets.Count == 0)
        {
            _output.WriteLine("No ticket folders found in the working folder.");
            return ExitCode.Ok;
        }

        if (Choose("Ticket", tickets, ticket => ticket) is not { } chosenTicket)
        {
            return Cancelled();
        }

        _output.WriteLine();

        // Dry first: prints both plans, connects to nothing. Its exit code is
        // ignored because a plan that cannot be built will say so and the real
        // run below will fail the same way, with the same message.
        _deploy.Execute(
            chosenClient.Id,
            environment.Name,
            chosenTicket,
            new DeployOptions(DryRun: true, Confirmed: false, TakeBackups: true));

        if (!Confirm(chosenClient, environment))
        {
            return Cancelled();
        }

        _output.WriteLine();

        return _deploy.Execute(
            chosenClient.Id,
            environment.Name,
            chosenTicket,
            new DeployOptions(DryRun: false, Confirmed: true, TakeBackups: true));
    }

    /// <summary>An environment as the picker shows it, aliases and all.</summary>
    private static string Describe(QadEnvironment environment) =>
        environment.IsProduction
            ? $"{environment.Described}   ** PRODUCTION **"
            : environment.Described;

    /// <summary>
    /// Asks the operator to pick one of <paramref name="options"/>, or nothing.
    /// </summary>
    /// <remarks>
    /// Re-asks on anything unrecognised rather than aborting, because a typo in
    /// a menu should cost a keystroke. A blank line cancels, and so does the end
    /// of input - which is how this behaves when stdin is not a console and
    /// there is nobody there to answer.
    /// </remarks>
    private T? Choose<T>(string prompt, IReadOnlyList<T> options, Func<T, string> describe)
        where T : class
    {
        // Nothing to choose between is not a question. Asking it would invite
        // the operator to wonder what the other options were.
        if (options.Count == 1)
        {
            _output.WriteLine($"{prompt}: {describe(options[0])}");
            _output.WriteLine();
            return options[0];
        }

        while (true)
        {
            _output.WriteLine($"{prompt}:");

            for (var i = 0; i < options.Count; i++)
            {
                _output.WriteLine($"  {i + 1}) {describe(options[i])}");
            }

            _output.WriteLine();
            _output.Write("> ");

            var answer = _input.ReadLine();

            if (string.IsNullOrWhiteSpace(answer))
            {
                return null;
            }

            if (int.TryParse(answer.Trim(), out var picked) && picked >= 1 && picked <= options.Count)
            {
                _output.WriteLine();
                return options[picked - 1];
            }

            _output.WriteLine($"'{answer.Trim()}' is not one of the numbers above.");
            _output.WriteLine();
        }
    }

    /// <summary>
    /// The last gate before anything is written.
    /// </summary>
    /// <remarks>
    /// Production asks for the environment's name rather than a keystroke. The
    /// command line demands <c>--yes</c> for the same reason: the point is an
    /// act that cannot be performed by reflex, and <c>y</c> at a prompt is
    /// exactly the sort of thing a tired person types without reading.
    /// </remarks>
    private bool Confirm(ClientProfile client, QadEnvironment environment)
    {
        _output.WriteLine();

        if (!environment.IsProduction)
        {
            _output.Write($"Deploy to {client.DisplayName} {environment.Name}? [y/N] ");

            return _input.ReadLine()?.Trim() is "y" or "Y";
        }

        _output.WriteLine($"This is {client.DisplayName} PRODUCTION.");
        _output.Write($"Type {environment.Name} to go ahead, or anything else to cancel: ");

        return string.Equals(_input.ReadLine()?.Trim(), environment.Name, StringComparison.Ordinal);
    }

    private int Cancelled()
    {
        _output.WriteLine();
        _output.WriteLine("Cancelled. Nothing was uploaded or compiled.");

        // Not an error: choosing not to deploy is a normal outcome, and a
        // non-zero code here would make a wrapper script look like it failed.
        return ExitCode.Ok;
    }
}
