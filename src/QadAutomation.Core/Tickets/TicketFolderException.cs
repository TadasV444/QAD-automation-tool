namespace QadAutomation.Core.Tickets;

/// <summary>
/// Raised when a ticket folder cannot be located or read.
/// </summary>
/// <remarks>
/// Like <c>ConfigurationException</c>, this exists so the CLI can distinguish
/// "the operator made a typo" from "the tool has a bug" and report accordingly.
/// </remarks>
public sealed class TicketFolderException : Exception
{
    public TicketFolderException(string message) : base(message)
    {
    }

    public TicketFolderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
