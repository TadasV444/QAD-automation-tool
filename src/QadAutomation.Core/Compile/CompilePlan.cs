using QadAutomation.Core.Configuration;
using QadAutomation.Core.Tickets;

namespace QadAutomation.Core.Compile;

/// <summary>
/// One program to compile, and how to tell whether it worked.
/// </summary>
/// <remarks>
/// The two kinds are separate types rather than one record with nullable fields
/// because they genuinely differ: a QRF report is compiled by typing a statement
/// and produces one result, while a SRC program is compiled by a batch script
/// and produces one result per language. A single shape would need
/// <c>Statement</c> to be null half the time, and the compiler would have to
/// re-check what it already knows.
/// </remarks>
/// <param name="File">The local file, for its name and kind.</param>
/// <param name="RemoteFile">The uploaded source on the server.</param>
/// <param name="RemoteResults">
/// Every compiled file this should produce, read before and after to decide the
/// verdict. All of them must move for the compile to count.
/// </param>
public abstract record PlannedCompile(
    ProgramFile File,
    string RemoteFile,
    IReadOnlyList<string> RemoteResults)
{
    public ProgramKind Kind => File.Kind;
}

/// <summary>A QRF report, compiled by typing a statement into the editor.</summary>
/// <param name="File">The local file, for its name and kind.</param>
/// <param name="RemoteFile">The uploaded source on the server.</param>
/// <param name="RemoteDirectory">Where the editor is told to save the result.</param>
/// <param name="RemoteResult">
/// The <c>.r</c> beside the source. Derived by swapping the extension, which is
/// what Progress does.
/// </param>
/// <param name="Statement">The exact text that will be typed.</param>
public sealed record PlannedQrfCompile(
    ProgramFile File,
    string RemoteFile,
    string RemoteDirectory,
    string RemoteResult,
    string Statement)
    : PlannedCompile(File, RemoteFile, [RemoteResult]);

/// <summary>
/// A SRC program, compiled by the batch script once per language.
/// </summary>
/// <param name="File">The local file, for its name and kind.</param>
/// <param name="RemoteFile">The uploaded source on the server.</param>
/// <param name="Results">
/// Language code to the <c>.r</c> it produces. Kept keyed by language so a
/// half-successful compile can name which one failed.
/// </param>
public sealed record PlannedSrcCompile(
    ProgramFile File,
    string RemoteFile,
    IReadOnlyDictionary<string, string> Results)
    : PlannedCompile(File, RemoteFile, [.. Results.Values]);

/// <summary>A program in the ticket that will not be compiled, and why.</summary>
public sealed record SkippedProgram(ProgramFile File, string Reason);

/// <summary>
/// Everything a compile run will do, worked out before anything connects.
/// </summary>
/// <remarks>
/// <para>
/// Same split as <see cref="Transfer.UploadPlan"/>, for the same reason: the
/// decisions worth checking - which files, which statement, which result - are
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
    IReadOnlyList<PlannedQrfCompile> Qrf,
    IReadOnlyList<PlannedSrcCompile> Src,
    IReadOnlyList<SkippedProgram> Skipped)
{
    /// <summary>Everything to compile, in a stable order, for display and reporting.</summary>
    public IReadOnlyList<PlannedCompile> Compiles => [.. Src.Cast<PlannedCompile>(), .. Qrf];

    /// <summary>Nothing will be compiled - though there may still be skips to report.</summary>
    public bool IsEmpty => Qrf.Count == 0 && Src.Count == 0;

    /// <summary>Whether this plan compiles on a production server.</summary>
    public bool IsProduction => Environment.IsProduction;

    /// <summary>
    /// Works out what can be compiled in <paramref name="ticket"/>.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="Transfer.UploadPlan.Create"/> this never throws for an
    /// unsupported program. An upload with no destination is a config error that
    /// must stop the run; a program with no compile recipe is a normal state,
    /// because a client can be configured for one kind and not the other. It is
    /// reported, and the other half still runs.
    /// </remarks>
    public static CompilePlan Create(TicketFolder ticket, QadEnvironment environment, string clientId)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(environment);

        var qrf = new List<PlannedQrfCompile>();
        var src = new List<PlannedSrcCompile>();
        var skipped = new List<SkippedProgram>();

        foreach (var file in ticket.Files)
        {
            switch (file.Kind)
            {
                case ProgramKind.Qrf:
                    PlanQrf(file, environment, qrf, skipped);
                    break;

                case ProgramKind.Src:
                    PlanSrc(file, environment, src, skipped);
                    break;

                default:
                    skipped.Add(new SkippedProgram(
                        file,
                        $"{file.Kind} programs have no compile procedure."));
                    break;
            }
        }

        return new CompilePlan(clientId, ticket.Name, environment, qrf, src, skipped);
    }

    private static void PlanQrf(
        ProgramFile file,
        QadEnvironment environment,
        List<PlannedQrfCompile> planned,
        List<SkippedProgram> skipped)
    {
        if (environment.Compile.Qrf is not { } recipe)
        {
            skipped.Add(new SkippedProgram(
                file, $"'{environment.Name}' has no 'compile.qrf' block in config.json."));
            return;
        }

        if (environment.Paths.Qrf is not { } directory)
        {
            skipped.Add(new SkippedProgram(
                file, $"'{environment.Name}' has no 'qrfRemotePath', so there is nothing to compile."));
            return;
        }

        directory = directory.TrimEnd('/');

        var remoteFile = $"{directory}/{file.FileName}";

        planned.Add(new PlannedQrfCompile(
            file,
            remoteFile,
            directory,
            SwapExtension(remoteFile),
            recipe.StatementFor(remoteFile, directory)));
    }

    private static void PlanSrc(
        ProgramFile file,
        QadEnvironment environment,
        List<PlannedSrcCompile> planned,
        List<SkippedProgram> skipped)
    {
        if (environment.Compile.Src is not { } recipe)
        {
            skipped.Add(new SkippedProgram(
                file, $"'{environment.Name}' has no 'compile.src' block in config.json."));
            return;
        }

        if (environment.Paths.Src is not { } directory)
        {
            skipped.Add(new SkippedProgram(
                file, $"'{environment.Name}' has no 'srcRemotePath', so there is nothing to compile."));
            return;
        }

        if (PrefixOf(file.FileName) is not { } prefix)
        {
            skipped.Add(new SkippedProgram(
                file,
                $"'{file.FileName}' is too short to have a two-letter prefix, " +
                "so its compiled output cannot be located."));
            return;
        }

        var remoteFile = $"{directory.TrimEnd('/')}/{file.FileName}";

        var results = recipe.Languages.ToDictionary(
            language => language.Key,
            language => $"{language.Value.TrimEnd('/')}/{prefix}/{SwapExtension(file.FileName)}",
            StringComparer.Ordinal);

        planned.Add(new PlannedSrcCompile(file, remoteFile, results));
    }

    /// <summary>
    /// The folder a SRC program's compiled output lands in, taken from the first
    /// two characters of its name.
    /// </summary>
    /// <remarks>
    /// <c>xx</c> marks a custom program, and the site's other prefixes work the
    /// same way. The valid set is deliberately <b>not</b> listed here or in
    /// config: the compiler checks the directory exists on the server instead, so
    /// the server stays the source of truth and no list can quietly go stale.
    /// </remarks>
    private static string? PrefixOf(string fileName)
    {
        var dot = fileName.IndexOf('.');
        var name = dot > 0 ? fileName[..dot] : fileName;

        // Letters and digits only - l4, l5 and l6 are real prefixes, but a name
        // like 'x.p' has nothing to take two characters from, and reading the
        // dot as part of a folder name would point at a directory that cannot
        // exist.
        return name.Length >= 2 && char.IsLetterOrDigit(name[0]) && char.IsLetterOrDigit(name[1])
            ? name[..2].ToLowerInvariant()
            : null;
    }

    /// <summary>
    /// Replaces a source file's extension with <c>.r</c>.
    /// </summary>
    /// <remarks>
    /// Built by string rather than with <c>Path</c> because these are POSIX
    /// remote paths and the tool runs on Windows, where <c>Path</c> would happily
    /// introduce a backslash. The dot must come after the last slash, or it
    /// belongs to a directory name rather than to the file.
    /// </remarks>
    private static string SwapExtension(string remotePath)
    {
        var lastDot = remotePath.LastIndexOf('.');
        var lastSlash = remotePath.LastIndexOf('/');

        return lastDot > lastSlash ? remotePath[..lastDot] + ".r" : remotePath + ".r";
    }
}
