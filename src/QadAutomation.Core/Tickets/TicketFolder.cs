namespace QadAutomation.Core.Tickets;

/// <summary>
/// The contents of one ticket folder, already classified into SRC and QRF.
/// </summary>
/// <param name="Name">The folder name, e.g. <c>Ticket 9999555</c>.</param>
/// <param name="Path">Absolute path to the ticket folder.</param>
/// <param name="Files">Everything found, in a stable order.</param>
public sealed record TicketFolder(
    string Name,
    string Path,
    IReadOnlyList<ProgramFile> Files)
{
    /// <summary>Files of a given kind, preserving discovery order.</summary>
    public IReadOnlyList<ProgramFile> OfKind(ProgramKind kind) =>
        Files.Where(f => f.Kind == kind).ToList();

    /// <summary>
    /// The kinds actually present. This is what drives the deployment: an upload
    /// step iterates the kinds found rather than assuming both exist.
    /// </summary>
    public IReadOnlyList<ProgramKind> KindsPresent =>
        Files.Select(f => f.Kind).Distinct().OrderBy(k => k).ToList();

    /// <summary>True when the ticket folder held no deployable files at all.</summary>
    public bool IsEmpty => Files.Count == 0;
}
