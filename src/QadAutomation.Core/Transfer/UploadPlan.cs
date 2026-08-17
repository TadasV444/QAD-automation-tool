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
    QadEnvironment Environment,
    IReadOnlyList<PlannedUpload> Uploads)
{
    /// <summary>Nothing to do. Not an error - an empty ticket folder is legal.</summary>
    public bool IsEmpty => Uploads.Count == 0;

    /// <summary>Whether this plan writes to production.</summary>
    public bool IsProduction => Environment.IsProduction;

    /// <summary>Distinct remote directories that will be written to.</summary>
    public IReadOnlyList<string> Destinations =>
        [.. Uploads.Select(u => u.RemoteDirectory).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

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

        return new UploadPlan(clientId, ticket.Name, environment, uploads);
    }
}
