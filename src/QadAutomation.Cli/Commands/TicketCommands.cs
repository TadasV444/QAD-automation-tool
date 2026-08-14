using QadAutomation.Core;
using QadAutomation.Core.Tickets;

namespace QadAutomation.Cli.Commands;

/// <summary>
/// <c>qad tickets</c> - lists the ticket folders in the working folder.
/// </summary>
public sealed class ListTicketsCommand
{
    private readonly ITicketFolderReader _reader;
    private readonly TextWriter _output;

    public ListTicketsCommand(ITicketFolderReader reader, TextWriter output)
    {
        _reader = reader;
        _output = output;
    }

    public int Execute()
    {
        var tickets = _reader.ListTickets();

        if (tickets.Count == 0)
        {
            _output.WriteLine("No ticket folders found.");
            return ExitCode.Ok;
        }

        foreach (var ticket in tickets)
        {
            _output.WriteLine(ticket);
        }

        return ExitCode.Ok;
    }
}

/// <summary>
/// <c>qad ticket &lt;id&gt;</c> - shows how one ticket folder's files were classified.
/// </summary>
/// <remarks>
/// The point of this command is to let the engineer see exactly what <i>would</i>
/// be uploaded, and as which kind, before any upload step exists. When the SFTP
/// step lands it will consume precisely this classification, so what is printed
/// here is what will happen - it is the dry run, arriving before the real thing.
/// </remarks>
public sealed class ShowTicketCommand
{
    private readonly ITicketFolderReader _reader;
    private readonly TextWriter _output;

    public ShowTicketCommand(ITicketFolderReader reader, TextWriter output)
    {
        _reader = reader;
        _output = output;
    }

    public int Execute(string ticket)
    {
        var folder = _reader.Read(ticket);

        _output.WriteLine($"Ticket: {folder.Name}");
        _output.WriteLine($"Path  : {folder.Path}");
        _output.WriteLine();

        if (folder.IsEmpty)
        {
            _output.WriteLine("No SRC or QRF files found.");
            _output.WriteLine("Expected a 'SRC' and/or 'QRF' sub-folder containing the program files.");

            // Not an error: an empty ticket folder is a normal state, and the
            // operator asked a question and got a truthful answer.
            return ExitCode.Ok;
        }

        foreach (var kind in folder.KindsPresent)
        {
            var files = folder.OfKind(kind);
            _output.WriteLine($"{kind.ToString().ToUpperInvariant()} ({files.Count} file(s)):");

            foreach (var file in files)
            {
                _output.WriteLine($"  {file.FileName}");
            }

            _output.WriteLine();
        }

        var missing = Enum.GetValues<ProgramKind>().Except(folder.KindsPresent).ToList();
        if (missing.Count > 0)
        {
            _output.WriteLine(
                $"No {string.Join("/", missing.Select(k => k.ToString().ToUpperInvariant()))} " +
                $"folder in this ticket - nothing of that kind will be deployed.");
        }

        return ExitCode.Ok;
    }
}
