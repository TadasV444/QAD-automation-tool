namespace QadAutomation.Core.Tickets;

/// <summary>
/// Discovers ticket folders in the local working folder and classifies their
/// contents.
/// </summary>
public interface ITicketFolderReader
{
    /// <summary>
    /// Ticket folder names under the working folder, sorted.
    /// </summary>
    /// <exception cref="TicketFolderException">If the working folder is missing.</exception>
    IReadOnlyList<string> ListTickets();

    /// <summary>
    /// Reads one ticket folder, classifying its files by sub-folder.
    /// </summary>
    /// <param name="ticket">
    /// The folder name (<c>Ticket 9999555</c>) or a fragment that identifies it
    /// unambiguously (<c>9999555</c>).
    /// </param>
    /// <exception cref="TicketFolderException">
    /// If the ticket cannot be found, or matches more than one folder.
    /// </exception>
    TicketFolder Read(string ticket);
}
