namespace QadAutomation.Core.Tickets;

/// <summary>
/// Reads ticket folders from the local working folder.
/// </summary>
/// <remarks>
/// <para>
/// The layout it expects, as confirmed for this project:
/// </para>
/// <code>
/// QAD Tasks/
///   Ticket 9999555/
///     SRC/        &lt;- optional; everything in here is ProgramKind.Src
///     QRF/        &lt;- optional; everything in here is ProgramKind.Qrf
/// </code>
/// <para>
/// A ticket folder has an <c>SRC</c> folder, a <c>QRF</c> folder, or both, and the
/// folder name <i>is</i> the classification. Nothing is guessed from file
/// extensions, so a file physically cannot be routed to the wrong remote path -
/// the worst case is that it is not picked up at all, which is visible and safe.
/// </para>
/// <para>
/// There is no <c>IFileSystem</c> abstraction here on purpose. This class is a
/// thin, honest wrapper over <c>System.IO</c>; mocking the filesystem would mostly
/// test the mock. The tests instead build real folders in a temporary directory,
/// which also exercises the real case-insensitivity and ordering behaviour.
/// </para>
/// </remarks>
public sealed class TicketFolderReader : ITicketFolderReader
{
    /// <summary>Sub-folder name to program kind. Matched case-insensitively.</summary>
    private static readonly IReadOnlyDictionary<string, ProgramKind> KindFolders =
        new Dictionary<string, ProgramKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["SRC"] = ProgramKind.Src,
            ["QRF"] = ProgramKind.Qrf
        };

    private readonly string _workingFolder;

    /// <param name="workingFolder">The local "QAD Tasks" folder.</param>
    public TicketFolderReader(string workingFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingFolder);
        _workingFolder = Path.GetFullPath(workingFolder);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListTickets()
    {
        RequireWorkingFolder();

        return Directory.EnumerateDirectories(_workingFolder)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc />
    public TicketFolder Read(string ticket)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticket);
        RequireWorkingFolder();

        var folder = ResolveTicketFolder(ticket.Trim());
        var files = new List<ProgramFile>();

        // Iterating the known kinds (rather than whatever directories happen to
        // exist) means an unexpected sub-folder is ignored instead of being
        // deployed somewhere arbitrary.
        foreach (var (folderName, kind) in KindFolders)
        {
            var kindFolder = FindSubFolder(folder, folderName);
            if (kindFolder is null)
            {
                continue;
            }

            // Top level only. A nested folder inside SRC/ has no defined remote
            // destination, so recursing would be inventing behaviour.
            var found = Directory.EnumerateFiles(kindFolder)
                .Where(IsDeployable)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new ProgramFile(kind, path));

            files.AddRange(found);
        }

        return new TicketFolder(
            Path.GetFileName(folder),
            folder,
            files.OrderBy(f => f.Kind).ThenBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>
    /// Resolves a ticket argument to exactly one folder.
    /// </summary>
    /// <remarks>
    /// An exact name match always wins. Otherwise a unique substring match is
    /// accepted, so <c>9999555</c> finds <c>Ticket 9999555</c>. An ambiguous
    /// fragment is an error rather than a coin flip - picking the "first" match
    /// could deploy the wrong ticket's files.
    /// </remarks>
    private string ResolveTicketFolder(string ticket)
    {
        var directories = Directory.GetDirectories(_workingFolder);

        var exact = directories.FirstOrDefault(
            d => string.Equals(Path.GetFileName(d), ticket, StringComparison.OrdinalIgnoreCase));

        if (exact is not null)
        {
            return exact;
        }

        var partial = directories
            .Where(d => Path.GetFileName(d).Contains(ticket, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return partial.Count switch
        {
            1 => partial[0],
            0 => throw new TicketFolderException(
                $"No ticket folder matching '{ticket}' under '{_workingFolder}'."),
            _ => throw new TicketFolderException(
                $"'{ticket}' matches {partial.Count} ticket folders: " +
                $"{string.Join(", ", partial.Select(Path.GetFileName))}. Be more specific.")
        };
    }

    /// <summary>
    /// Finds a sub-folder by name without assuming the filesystem's casing rules.
    /// </summary>
    private static string? FindSubFolder(string parent, string name) =>
        Directory.EnumerateDirectories(parent)
            .FirstOrDefault(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Filters out the debris that accumulates in working folders - Windows'
    /// <c>Thumbs.db</c>, editor swap files, and anything hidden.
    /// </summary>
    private static bool IsDeployable(string path)
    {
        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.System))
        {
            return false;
        }

        var name = Path.GetFileName(path);
        return !name.StartsWith('~') && !name.StartsWith('.');
    }

    private void RequireWorkingFolder()
    {
        if (!Directory.Exists(_workingFolder))
        {
            throw new TicketFolderException(
                $"The working folder '{_workingFolder}' does not exist. " +
                $"Check 'workingFolder' in your configuration.");
        }
    }
}
