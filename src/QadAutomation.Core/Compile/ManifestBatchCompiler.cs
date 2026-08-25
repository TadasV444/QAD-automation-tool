using QadAutomation.Core.Configuration;
using QadAutomation.Core.Transfer;

namespace QadAutomation.Core.Compile;

/// <summary>
/// Compiles SRC programs by writing the manifest and running the batch script.
/// </summary>
/// <remarks>
/// <para>
/// The whole ticket's SRC programs go in one manifest and are built by one run
/// of the script per language - unlike QRF, which is one editor statement per
/// file. So there is no per-file progress to report during the run, only the
/// verdicts afterwards.
/// </para>
/// <para>
/// <b>Both languages must succeed.</b> A SRC program compiles into <c>lt</c> and
/// <c>us</c> separately, and one landing without the other is a real state: half
/// the users get the new program and half keep the old one, which is worse than
/// an outright failure because nothing looks wrong. It is reported as a failure
/// naming the language that did not move.
/// </para>
/// <para>
/// <b>The manifest is a shared file.</b> One fixed path on the server, overwritten
/// each run, so two people compiling at the same moment would build each other's
/// list. Nothing here can prevent that - it is a property of the site's setup -
/// but it is why the manifest is written as late as possible, immediately before
/// the script runs.
/// </para>
/// <para>
/// <b>The script is not fire-and-forget.</b> It raises a long-standing warning
/// dialog and blocks on <c>&lt;OK&gt;</c>, so an Enter follows every command.
/// Found the hard way: the first real run typed the second language's command
/// into that dialog, which built <c>lt</c>, left <c>us</c> untouched, and was
/// reported - correctly - as a failure.
/// </para>
/// </remarks>
internal sealed class ManifestBatchCompiler
{
    /// <summary>
    /// How long output must stop before the script is considered finished.
    /// </summary>
    /// <remarks>
    /// Longer than the QRF editor's settle time. That one waits for a screen to
    /// redraw; this waits for a compiler to work through a list, and finishing
    /// early would read the timestamps before the results were written.
    /// </remarks>
    private static readonly TimeSpan SettleFor = TimeSpan.FromSeconds(3);

    /// <summary>Upper bound on one language's compile.</summary>
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);

    private readonly ISshShellFactory _shells;

    public ManifestBatchCompiler(ISshShellFactory shells) => _shells = shells;

    public IReadOnlyList<CompiledProgram> Compile(
        IReadOnlyList<PlannedManifestCompile> planned,
        ManifestCompileSettings recipe,
        ISftpSession session,
        SshEndpoint endpoint,
        Action<string> report)
    {
        VerifySourcesExist(planned, session);
        VerifyResultDirectoriesExist(planned, session);

        var before = planned.ToDictionary(
            compile => compile,
            compile => Timestamps(compile, session));

        var screen = RunScript(planned, recipe, session, endpoint, report);

        return [.. planned.Select(compile =>
        {
            var after = Timestamps(compile, session);

            var stale = compile.Results.Keys
                .Where(language => !Moved(before[compile][language], after[language]))
                .ToList();

            var result = stale.Count == 0 ? CompileResult.Compiled : CompileResult.Failed;

            report(stale.Count == 0
                ? $"  compiled {compile.File.FileName}"
                : $"  FAILED   {compile.File.FileName} ({string.Join(", ", stale)} did not build)");

            return new CompiledProgram(compile, result, screen);
        })];
    }

    /// <summary>
    /// Writes the manifest, then runs the script once per language.
    /// </summary>
    /// <remarks>
    /// The output of every language is captured together and attached to each
    /// program, because the script builds the whole list at once and there is no
    /// way to tell which line of output belongs to which program. That is only a
    /// presentation compromise - the verdicts come from the timestamps, which are
    /// per program and exact.
    /// </remarks>
    private string RunScript(
        IReadOnlyList<PlannedManifestCompile> planned,
        ManifestCompileSettings recipe,
        ISftpSession session,
        SshEndpoint endpoint,
        Action<string> report)
    {
        // One bare filename per line. Written immediately before the run, and
        // trailing newline included so the last entry is a complete line.
        var manifest = string.Concat(planned.Select(compile => compile.File.FileName + "\n"));

        // One site shares a single manifest between both languages; another
        // keeps one per language. The same list goes to each, so both are the
        // same act - which is why the paths are deduplicated rather than the
        // languages iterated.
        foreach (var path in recipe.ManifestPaths)
        {
            report($"Writing {planned.Count} program name(s) to {path}");
            session.WriteText(path, manifest);
        }

        using var shell = _shells.Open(endpoint);

        shell.ReadUntilIdle(TimeSpan.FromMilliseconds(750), CommandTimeout);

        shell.Send($"cd {recipe.WorkingDirectory}" + ProgressKeys.Enter);
        shell.ReadUntilIdle(TimeSpan.FromMilliseconds(750), CommandTimeout);

        var captured = new List<string>();

        foreach (var language in recipe.Languages.Keys)
        {
            var command = recipe.CommandFor(language);

            report($"  {command}");

            shell.Send(command + ProgressKeys.Enter);
            captured.Add(shell.ReadUntilIdle(SettleFor, CommandTimeout));

            // The script raises a harmless warning dialog and waits on <OK>.
            // Nothing after this point in the shell is read until it is
            // dismissed - the first real run typed the second language's
            // command straight into the dialog, so 'lt' built and 'us' did not.
            shell.Send(ProgressKeys.Enter);
            captured.Add(shell.ReadUntilIdle(SettleFor, CommandTimeout));
        }

        return string.Join("\n", captured);
    }

    private static Dictionary<string, DateTimeOffset?> Timestamps(
        PlannedManifestCompile compile, ISftpSession session) =>
        compile.Results.ToDictionary(
            result => result.Key,
            result => session.LastWriteTime(result.Value),
            StringComparer.Ordinal);

    private static bool Moved(DateTimeOffset? before, DateTimeOffset? after) =>
        ProgressEditorCompiler.Moved(before, after);

    private static void VerifySourcesExist(IReadOnlyList<PlannedManifestCompile> planned, ISftpSession session)
    {
        var missing = planned
            .Where(compile => !session.Exists(compile.RemoteFile))
            .Select(compile => compile.RemoteFile)
            .ToList();

        if (missing.Count > 0)
        {
            throw new TransferException(
                $"{missing.Count} file(s) are not on the server:{Environment.NewLine}" +
                string.Join(Environment.NewLine, missing.Select(p => "  - " + p)) +
                $"{Environment.NewLine}Run 'qad upload' first. Nothing was compiled.");
        }
    }

    /// <summary>
    /// Confirms every language's output directory is there before compiling.
    /// </summary>
    /// <remarks>
    /// The directory comes from the program's two-letter prefix, so a file named
    /// outside the site's convention points at a folder that does not exist.
    /// Caught here rather than after the run, where it would look like a compile
    /// failure and send someone hunting through Progress errors for a naming
    /// mistake.
    /// </remarks>
    private static void VerifyResultDirectoriesExist(IReadOnlyList<PlannedManifestCompile> planned, ISftpSession session)
    {
        var missing = planned
            .SelectMany(compile => compile.Results.Values)
            .Select(Directory)
            .Distinct(StringComparer.Ordinal)
            .Where(directory => !session.Exists(directory))
            .Order(StringComparer.Ordinal)
            .ToList();

        if (missing.Count > 0)
        {
            throw new TransferException(
                $"These compiled-output directories do not exist:{Environment.NewLine}" +
                string.Join(Environment.NewLine, missing.Select(d => "  - " + d)) +
                $"{Environment.NewLine}The folder comes from the program's first two letters. " +
                "Check the file names, and 'compile.src.languages' in config.json. " +
                "Nothing was compiled.");
        }
    }

    private static string Directory(string remotePath) =>
        remotePath[..remotePath.LastIndexOf('/')];
}
