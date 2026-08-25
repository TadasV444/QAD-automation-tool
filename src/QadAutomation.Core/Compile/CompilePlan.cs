using QadAutomation.Core.Configuration;
using QadAutomation.Core.Tickets;

namespace QadAutomation.Core.Compile;

/// <summary>
/// One program to compile, and how to tell whether it worked.
/// </summary>
/// <remarks>
/// One record per procedure rather than one shape with nullable fields. The
/// procedures need genuinely different things - a statement to type, a set of
/// per-language outputs, or neither - and a single record would leave every
/// consumer re-deciding which kind it was holding. What they do share is
/// <see cref="RemoteResults"/>: the artefacts whose timestamps decide the
/// verdict.
/// </remarks>
/// <param name="File">The local file, for its name and kind.</param>
/// <param name="RemoteFile">The uploaded source on the server.</param>
/// <param name="RemoteResults">
/// Every compiled file this should produce, read before and after. All of them
/// must move for the compile to count. May be empty, for a procedure whose
/// output location is unknown and which is judged by its exit code instead.
/// </param>
public abstract record PlannedCompile(
    ProgramFile File,
    string RemoteFile,
    IReadOnlyList<string> RemoteResults)
{
    public ProgramKind Kind => File.Kind;
}

/// <summary>A report compiled by typing a statement into the Progress editor.</summary>
/// <param name="File">The local file, for its name and kind.</param>
/// <param name="RemoteFile">The uploaded source on the server.</param>
/// <param name="RemoteDirectory">Where the editor is told to save the result.</param>
/// <param name="RemoteResult">
/// The <c>.r</c> beside the source. Derived by swapping the extension, which is
/// what Progress does.
/// </param>
/// <param name="Statement">The exact text that will be typed.</param>
public sealed record PlannedEditorCompile(
    ProgramFile File,
    string RemoteFile,
    string RemoteDirectory,
    string RemoteResult,
    string Statement)
    : PlannedCompile(File, RemoteFile, [RemoteResult]);

/// <summary>
/// A program compiled by the batch script, once per language.
/// </summary>
/// <param name="File">The local file, for its name and kind.</param>
/// <param name="RemoteFile">The uploaded source on the server.</param>
/// <param name="Results">
/// Language code to the <c>.r</c> it produces. Kept keyed by language so a
/// half-successful compile can name which one failed.
/// </param>
public sealed record PlannedManifestCompile(
    ProgramFile File,
    string RemoteFile,
    IReadOnlyDictionary<string, string> Results)
    : PlannedCompile(File, RemoteFile, [.. Results.Values]);

/// <summary>
/// A program compiled by an ordinary shell command that finds its own work.
/// </summary>
/// <param name="File">The local file, for its name and kind.</param>
/// <param name="RemoteFile">The uploaded source on the server.</param>
/// <param name="RemoteResult">
/// Where the command should write this program's output, or <c>null</c> when the
/// site has not told us - in which case the command's exit code is the only
/// signal available.
/// </param>
public sealed record PlannedShellCompile(
    ProgramFile File,
    string RemoteFile,
    string? RemoteResult)
    : PlannedCompile(File, RemoteFile, RemoteResult is null ? [] : [RemoteResult]);

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
    IReadOnlyList<PlannedCompile> Compiles,
    IReadOnlyList<SkippedProgram> Skipped)
{
    /// <summary>Nothing will be compiled - though there may still be skips to report.</summary>
    public bool IsEmpty => Compiles.Count == 0;

    /// <summary>Whether this plan compiles on a production server.</summary>
    public bool IsProduction => Environment.IsProduction;

    /// <summary>The entries driven by one procedure, in plan order.</summary>
    public IReadOnlyList<T> Using<T>() where T : PlannedCompile => [.. Compiles.OfType<T>()];

    /// <summary>Shell entries for one kind, which share a single command.</summary>
    public IReadOnlyList<PlannedShellCompile> Shell(ProgramKind kind) =>
        [.. Compiles.OfType<PlannedShellCompile>().Where(compile => compile.Kind == kind)];

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

        var compiles = new List<PlannedCompile>();
        var skipped = new List<SkippedProgram>();

        // SRC first, so a mixed ticket reaches the Progress editor last - it
        // never exits, and the shell it runs in is unusable afterwards.
        foreach (var file in ticket.Files.OrderBy(f => f.Kind == ProgramKind.Qrf))
        {
            Plan(file, environment, clientId, compiles, skipped);
        }

        return new CompilePlan(clientId, ticket.Name, environment, compiles, skipped);
    }

    private static void Plan(
        ProgramFile file,
        QadEnvironment environment,
        string clientId,
        List<PlannedCompile> compiles,
        List<SkippedProgram> skipped)
    {
        var (block, editor, manifest, shell) = file.Kind switch
        {
            ProgramKind.Qrf => (
                "compile.qrf",
                environment.Compile.Qrf?.Editor,
                null,
                environment.Compile.Qrf?.Shell),

            ProgramKind.Src => (
                "compile.src",
                (EditorCompileSettings?)null,
                environment.Compile.Src?.Manifest,
                environment.Compile.Src?.Shell),

            _ => throw new ArgumentOutOfRangeException(nameof(file), file.Kind, "Unknown program kind.")
        };

        if (editor is null && manifest is null && shell is null)
        {
            skipped.Add(new SkippedProgram(
                file, $"'{environment.Name}' has no '{block}' block in config.json."));
            return;
        }

        string directory;

        try
        {
            directory = environment.Paths.Require(file, clientId, environment.Name);
        }
        catch (ConfigurationException ex)
        {
            // The upload treats this as fatal; here it is one program that
            // cannot be compiled while the rest of the ticket still can.
            skipped.Add(new SkippedProgram(file, ex.Message));
            return;
        }

        directory = directory.TrimEnd('/');

        var remoteFile = $"{directory}/{file.FileName}";

        if (editor is not null)
        {
            compiles.Add(new PlannedEditorCompile(
                file,
                remoteFile,
                directory,
                SwapExtension(remoteFile),
                editor.StatementFor(remoteFile, directory)));

            return;
        }

        if (manifest is not null)
        {
            PlanManifest(file, manifest, remoteFile, compiles, skipped);
            return;
        }

        compiles.Add(new PlannedShellCompile(
            file,
            remoteFile,
            shell!.ResultFor(file.FileName, file.Prefix ?? string.Empty)));
    }

    private static void PlanManifest(
        ProgramFile file,
        ManifestCompileSettings recipe,
        string remoteFile,
        List<PlannedCompile> compiles,
        List<SkippedProgram> skipped)
    {
        if (file.Prefix is not { } prefix)
        {
            skipped.Add(new SkippedProgram(
                file,
                $"'{file.FileName}' is too short to have a two-letter prefix, " +
                "so its compiled output cannot be located."));
            return;
        }

        var results = recipe.Languages.ToDictionary(
            language => language.Key,
            language => $"{language.Value.ResultRoot.TrimEnd('/')}/{prefix}/{SwapExtension(file.FileName)}",
            StringComparer.Ordinal);

        compiles.Add(new PlannedManifestCompile(file, remoteFile, results));
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
