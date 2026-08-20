using QadAutomation.Core.Configuration;
using QadAutomation.Core.Tickets;

namespace QadAutomation.Core.Transfer;

/// <summary>
/// Everything an upload will do, worked out before anything is connected.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why planning is separated from doing.</b> Deciding where each file goes is
/// the part that can silently be wrong - a SRC program landing in the QRF
/// directory is exactly the accident this tool exists to prevent, and it is not
/// the sort of mistake that announces itself. Separating the decision from the
/// transfer buys three things:
/// </para>
/// <list type="bullet">
///   <item>the routing logic is a pure function, so every case is unit-testable
///         with no server, no VPN and no credentials;</item>
///   <item><c>--dry-run</c> comes free and is honest, because it prints the very
///         object the upload consumes rather than a description of it;</item>
///   <item>a missing remote path fails before the VPN is dialled, instead of
///         halfway through a batch with some files already written.</item>
/// </list>
/// <para>
/// Nothing in this type performs I/O. It does not even check that the local
/// files still exist - that is the uploader's problem, at the moment it matters.
/// </para>
/// </remarks>
public sealed record UploadPlan(
    string ClientId,
    string TicketName,
    string TicketPath,
    QadEnvironment Environment,
    IReadOnlyList<PlannedUpload> Uploads)
{
    /// <summary>
    /// Folder inside the ticket that holds downloaded backups of replaced files.
    /// </summary>
    /// <remarks>
    /// Deliberately a sibling of <c>SRC</c> and <c>QRF</c> rather than a child of
    /// them. <see cref="Tickets.TicketFolderReader"/> only reads the top level of
    /// each kind folder, so a backup inside <c>QRF/</c> would be safe today - but
    /// it would become a file queued for upload the moment anyone taught the
    /// reader to recurse. Keeping backups out of the kind folders entirely means
    /// last week's version can never be re-deployed by accident.
    /// </remarks>
    public const string BackupFolderName = "_backup";

    /// <summary>Nothing to do. Not an error - an empty ticket folder is legal.</summary>
    public bool IsEmpty => Uploads.Count == 0;

    /// <summary>Whether this plan writes to production.</summary>
    public bool IsProduction => Environment.IsProduction;

    /// <summary>Distinct remote directories that will be written to.</summary>
    public IReadOnlyList<string> Destinations =>
        [.. Uploads.Select(u => u.RemoteDirectory).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>
    /// Where this run's backups go: <c>&lt;ticket&gt;\_backup\&lt;ENV&gt;-&lt;stamp&gt;</c>.
    /// </summary>
    /// <remarks>
    /// The environment is in the name because the same ticket is normally
    /// deployed to TEST and then to PROD, and those two runs replace different
    /// files. A stamp alone would put both in sibling folders with nothing to say
    /// which server each came from - exactly the question being asked when
    /// somebody goes looking for a backup.
    /// </remarks>
    public string BackupFolder(string stamp) =>
        Path.Combine(TicketPath, BackupFolderName, $"{Environment.Name}-{stamp}");

    /// <summary>
    /// Local path a backup of <paramref name="upload"/> would be written to.
    /// </summary>
    /// <remarks>
    /// The kind is a sub-folder, and the file keeps its original name. Both are
    /// for the restore: an operator putting a file back copies it over the one in
    /// <c>SRC\</c> or <c>QRF\</c>, and a name mangled with a timestamp would have
    /// to be corrected by hand first. The kind folder also keeps a SRC and a QRF
    /// program of the same name from overwriting one another's backup.
    /// </remarks>
    public string BackupPathFor(PlannedUpload upload, string stamp) =>
        Path.Combine(
            BackupFolder(stamp),
            upload.Kind.ToString().ToUpperInvariant(),
            upload.File.FileName);

    /// <summary>
    /// Works out where every file in <paramref name="ticket"/> belongs.
    /// </summary>
    /// <exception cref="ConfigurationException">
    /// If the ticket contains a kind of program the environment has no remote
    /// path for. Deliberately a hard failure: the alternatives are skipping the
    /// file silently or guessing a destination, and both are worse than stopping.
    /// </exception>
    public static UploadPlan Create(TicketFolder ticket, QadEnvironment environment, string clientId)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(environment);

        var uploads = ticket.Files
            .Select(file => new PlannedUpload(
                file,
                environment.Paths.Require(file.Kind, clientId, environment.Name)))
            .ToList();

        return new UploadPlan(clientId, ticket.Name, ticket.Path, environment, uploads);
    }
}
