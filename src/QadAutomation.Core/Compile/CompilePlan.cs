using QadAutomation.Core.Configuration;
using QadAutomation.Core.Tickets;

namespace QadAutomation.Core.Compile;

/// <summary>
/// One program to compile, and how to tell whether it worked.
/// </summary>
/// <param name="File">The local file, for its name and kind.</param>
/// <param name="RemoteFile">The uploaded <c>.p</c> on the server.</param>
/// <param name="RemoteDirectory">Where the compiler is told to save the result.</param>
/// <param name="RemoteResult">
/// The <c>.r</c> this should produce. Derived by swapping the extension, which
/// is what Progress does, and read before and after to decide the verdict.
/// </param>
/// <param name="Statement">The exact text that will be typed into the editor.</param>
public sealed record PlannedCompile(
    ProgramFile File,
    string RemoteFile,
    string RemoteDirectory,
    string RemoteResult,
    string Statement)
{
    public ProgramKind Kind => File.Kind;
}

/// <summary>A program in the ticket that will not be compiled, and why.</summary>
public sealed record SkippedProgram(ProgramFile File, string Reason);

/// <summary>
/// Everything a compile run will do, worked out before anything connects.
/// </summary>
/// <remarks>
/// <para>
/// Same split as <see cref="Transfer.UploadPlan"/>, for the same reason: the
/// decisions worth checking - which files, which statement, which <c>.r</c> - are
/// made by a pure function that needs no server, and <c>--dry-run</c> prints the
/// very object the run consumes rather than a description of it.
/// </para>
/// <para>
/// <b>Skipping is explicit.</b> A program the tool cannot compile goes into
/// <see cref="Skipped"/> with a reason rather than being quietly dropped. The
/// dangerous outcome here is not an error; it is an operator believing a ticket
/// is deployed when half of it was never built.
/// </para>
/// </remarks>
public sealed record CompilePlan(
    string ClientId,
    string TicketName,
    QadEnvironment Environment,
    IReadOnlyList<PlannedCompile> Compiles,
    IReadOnlyList<SkippedProgram> Skipped)
{
    /// <summary>Nothing will be compiled - though there may still be skips to report.</summary>
    public bool IsEmpty => Compiles.Count == 0;

    /// <summary>Whether this plan compiles on a production server.</summary>
    public bool IsProduction => Environment.IsProduction;

    /// <summary>
    /// Works out what can be compiled in <paramref name="ticket"/>.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="Transfer.UploadPlan.Create"/> this never throws for an
    /// unsupported program. An upload with no destination is a config error that
    /// must stop the run; a program with no compile recipe is a normal state
    /// today, because SRC has no verified recipe anywhere. It is reported, and
    /// the QRF half still runs.
    /// </remarks>
    public static CompilePlan Create(TicketFolder ticket, QadEnvironment environment, string clientId)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(environment);

        var compiles = new List<PlannedCompile>();
        var skipped = new List<SkippedProgram>();

        foreach (var file in ticket.Files)
        {
            if (file.Kind != ProgramKind.Qrf)
            {
                skipped.Add(new SkippedProgram(
                    file,
                    $"{file.Kind.ToString().ToUpperInvariant()} compilation is not implemented yet - compile it by hand."));
                continue;
            }

            if (environment.Compile.Qrf is not { } recipe)
            {
                skipped.Add(new SkippedProgram(
                    file,
                    $"'{environment.Name}' has no 'compile.qrf' block in config.json."));
                continue;
            }

            var directory = environment.Paths.Qrf;

            if (directory is null)
            {
                skipped.Add(new SkippedProgram(
                    file,
                    $"'{environment.Name}' has no 'qrfRemotePath', so there is nothing to compile."));
                continue;
            }

            var remoteFile = $"{directory.TrimEnd('/')}/{file.FileName}";

            compiles.Add(new PlannedCompile(
                file,
                remoteFile,
                directory.TrimEnd('/'),
                ResultPathFor(remoteFile),
                recipe.StatementFor(remoteFile, directory.TrimEnd('/'))));
        }

        return new CompilePlan(clientId, ticket.Name, environment, compiles, skipped);
    }

    /// <summary>
    /// The <c>.r</c> Progress will write for a given source file.
    /// </summary>
    /// <remarks>
    /// Built by string rather than with <c>Path</c> because these are POSIX
    /// remote paths and the tool runs on Windows, where <c>Path</c> would happily
    /// introduce a backslash.
    /// </remarks>
    private static string ResultPathFor(string remoteFile)
    {
        var lastDot = remoteFile.LastIndexOf('.');
        var lastSlash = remoteFile.LastIndexOf('/');

        // A dot before the last slash belongs to a directory name, not the file.
        return lastDot > lastSlash ? remoteFile[..lastDot] + ".r" : remoteFile + ".r";
    }
}
