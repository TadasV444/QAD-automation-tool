namespace QadAutomation.Core.Configuration;

/// <summary>
/// How each kind of program is compiled on the remote host.
/// </summary>
/// <remarks>
/// <para>
/// Both halves are optional. An environment with no <c>src</c> block simply
/// cannot compile SRC, and says so when asked; requiring a recipe that has never
/// been run against a real server would only invite an invented one.
/// </para>
/// <para>
/// Each half then names <b>which</b> procedure the site uses. Two clients have
/// needed four between them - a Progress editor driven by function keys, a
/// manifest file plus a per-language script, and a plain shell command for
/// either kind - and there is no reason to expect the fourth to be the last.
/// </para>
/// <para>
/// There is still no strategy enum. Naming the procedure and supplying its
/// settings are one act here: you cannot write <c>"editor"</c> and then fill in
/// a shell command's fields, which a discriminator plus a flat field list would
/// have allowed.
/// </para>
/// </remarks>
/// <param name="Qrf">Recipe for QRF reports, or <c>null</c> if not configured.</param>
/// <param name="Src">Recipe for SRC programs, or <c>null</c> if not configured.</param>
public sealed record CompileSettings(QrfCompileSettings? Qrf, SrcCompileSettings? Src)
{
    /// <summary>Nothing here can be compiled.</summary>
    public bool IsEmpty => Qrf is null && Src is null;
}

/// <summary>
/// How this site compiles QRF reports. Exactly one procedure is set.
/// </summary>
public sealed record QrfCompileSettings(
    EditorCompileSettings? Editor,
    ShellCompileSettings? Shell);

/// <summary>
/// How this site compiles SRC programs. Exactly one procedure is set.
/// </summary>
public sealed record SrcCompileSettings(
    ManifestCompileSettings? Manifest,
    ShellCompileSettings? Shell);

/// <summary>
/// Compiling a report through an interactive editor wrapper.
/// </summary>
/// <remarks>
/// <para>
/// <b>The keystrokes are config, and that is a reversal.</b> With one site they
/// lived in code, on the reasoning that F4 and F1 are properties of the Progress
/// editor rather than of any installation. A second site disproved it: there the
/// wrapper is a different script, entered with Enter rather than F4, and needing
/// F1 twice - once to leave the input window and once to compile. The keys turn
/// out to belong to the client's wrapper, not to Progress, so they belong here.
/// </para>
/// <para>
/// <see cref="Steps"/> names them symbolically. Nobody should be typing escape
/// sequences into a JSON file to describe a function key.
/// </para>
/// </remarks>
/// <param name="EditorCommand">
/// The command that opens the editor, with <c>{language}</c> substituted where
/// the wrapper takes one. Held whole rather than assembled from parts because
/// its arguments are the site's and a wrong guess in code cannot be corrected
/// without a rebuild.
/// </param>
/// <param name="WorkingDirectory">
/// Directory to change to first, or <c>null</c> where the command is absolute.
/// </param>
/// <param name="Languages">
/// Language codes to run the whole procedure once for each of, or empty where
/// the wrapper is language-neutral.
/// </param>
/// <param name="Steps">
/// What to send for each program, in order. <see cref="EditorStep.Statement"/>
/// stands for the text; everything else is a keystroke.
/// </param>
/// <param name="RestartPerFile">
/// Whether the editor must be reopened for each program. False where a key in
/// <see cref="Steps"/> clears the buffer and the session can be reused.
/// </param>
/// <param name="StatementTemplate">
/// The text to type, with <c>{remoteFile}</c> and <c>{remoteDirectory}</c>
/// substituted per file. May be a whole Progress statement or just a path,
/// depending on what the wrapper is asking for.
/// </param>
public sealed record EditorCompileSettings(
    string EditorCommand,
    string? WorkingDirectory,
    IReadOnlyList<string> Languages,
    IReadOnlyList<EditorStep> Steps,
    bool RestartPerFile,
    string StatementTemplate)
{
    /// <summary>The Progress <c>COMPILE</c> statement, which most sites type.</summary>
    public const string DefaultStatementTemplate = "compile {remoteFile} save into {remoteDirectory}.";

    /// <summary>
    /// The first site's editor: F4 to clear the buffer, type, Return, F1 to run.
    /// </summary>
    /// <remarks>
    /// The Return is listed rather than implied by <see cref="EditorStep.Statement"/>,
    /// because the second site's wrapper must <i>not</i> have one - there a
    /// function key leaves the input window, and a stray newline first is read
    /// as an empty second entry.
    /// </remarks>
    public static readonly IReadOnlyList<EditorStep> DefaultSteps =
        [EditorStep.NewBuffer, EditorStep.Statement, EditorStep.Enter, EditorStep.Go];

    /// <summary>The command that opens the editor for one language.</summary>
    public string CommandFor(string language) =>
        EditorCommand.Replace("{language}", language, StringComparison.Ordinal);

    /// <summary>The text to type for one report.</summary>
    public string StatementFor(string remoteFile, string remoteDirectory) =>
        StatementTemplate
            .Replace("{remoteFile}", remoteFile, StringComparison.Ordinal)
            .Replace("{remoteDirectory}", remoteDirectory, StringComparison.Ordinal);
}

/// <summary>
/// One thing to send to an editor: a keystroke, or the text itself.
/// </summary>
/// <remarks>
/// A closed set rather than free text. An operator writing raw escape sequences
/// into config would be guessing at bytes no editor shows them, and a wrong
/// guess is silently ignored by the far end - indistinguishable from a compile
/// that did nothing.
/// </remarks>
public enum EditorStep
{
    /// <summary>The statement or path this program needs.</summary>
    Statement,

    /// <summary>Return.</summary>
    Enter,

    /// <summary>F1. Runs the buffer on one site; on another, leaves a window.</summary>
    Go,

    /// <summary>F4. Clears the buffer for the next program.</summary>
    NewBuffer
}

/// <summary>
/// Compiling a whole batch listed in a manifest file, once per language.
/// </summary>
/// <remarks>
/// <para>
/// A single program compiled this way produces <b>one result per language</b>,
/// in different directory trees. That is why the languages are a map rather than
/// a list of command strings: each entry supplies both the command to run and
/// the place to check afterwards, so the two cannot drift apart. Kept as separate
/// settings they could, and the failure mode would be a compile reported against
/// a directory nothing ever writes to.
/// </para>
/// <para>
/// The script is not fire-and-forget - it raises a warning dialog and blocks on
/// <c>&lt;OK&gt;</c>, so the compiler sends Enter after each command.
/// </para>
/// </remarks>
/// <param name="WorkingDirectory">Directory the commands are run from.</param>
/// <param name="CommandTemplate">
/// Run once per language with <c>{language}</c> substituted, e.g.
/// <c>./compile {language} test</c>. The environment, where the script takes
/// one, is part of the template rather than a separate field because it is the
/// script's own argument and its spelling is the site's, not this tool's.
/// </param>
/// <param name="Languages">Language code to where that language reads and writes.</param>
public sealed record ManifestCompileSettings(
    string WorkingDirectory,
    string CommandTemplate,
    IReadOnlyDictionary<string, LanguageTarget> Languages)
{
    /// <summary>The command to run for one language.</summary>
    public string CommandFor(string language) =>
        CommandTemplate.Replace("{language}", language, StringComparison.Ordinal);

    /// <summary>Every distinct manifest file this recipe writes.</summary>
    /// <remarks>
    /// Distinct, because one site points both languages at a single shared file
    /// and writing it twice would be pointless work reported twice.
    /// </remarks>
    public IReadOnlyList<string> ManifestPaths =>
        [.. Languages.Values.Select(target => target.ManifestPath).Distinct(StringComparer.Ordinal)];
}

/// <summary>
/// Where one language reads its list of work and writes its results.
/// </summary>
/// <remarks>
/// <para>
/// The two travel together because they are the same language's two halves, and
/// separating them is how a site could end up writing one language's manifest
/// while checking another's output.
/// </para>
/// <para>
/// One observed site shares a single manifest between both languages; another
/// keeps one per language beside that language's output. Naming the path per
/// language covers both, where a single shared setting could only cover the
/// first.
/// </para>
/// </remarks>
/// <param name="ManifestPath">
/// Remote file listing the programs to compile, one bare filename per line,
/// overwritten each run. Note it is a fixed path shared with everyone else who
/// compiles on that server: two people at once would overwrite each other's list.
/// </param>
/// <param name="ResultRoot">
/// Root directory this language's compiled output lands under. The results
/// themselves sit one level deeper, in a folder named after the program's prefix.
/// </param>
public sealed record LanguageTarget(string ManifestPath, string ResultRoot);

/// <summary>
/// Compiling by running one ordinary shell command.
/// </summary>
/// <remarks>
/// <para>
/// The simplest of the procedures and the only one that works for either kind:
/// the site provides a build script that finds the changed programs itself, so
/// nothing needs listing, typing or per-file substitution.
/// </para>
/// <para>
/// It is also the only one that can be verified by an <b>exit code</b> rather
/// than by inspecting what it wrote, since a plain command has one to report.
/// Whether a given script sets it meaningfully is a property of that script, so
/// <see cref="ResultPath"/> exists for the case where it does not.
/// </para>
/// </remarks>
/// <param name="WorkingDirectory">Directory the command is run from.</param>
/// <param name="Command">The command, run exactly as written.</param>
/// <param name="ResultPath">
/// Optional. Where this command writes each program's compiled output, with
/// <c>{prefix}</c> and <c>{name}</c> substituted per file. When set, the
/// timestamps there decide the verdict and the exit code is only reported;
/// when absent, the exit code alone decides.
/// </param>
public sealed record ShellCompileSettings(
    string WorkingDirectory,
    string Command,
    string? ResultPath)
{
    /// <summary>Where <paramref name="fileName"/>'s compiled output should appear.</summary>
    public string? ResultFor(string fileName, string prefix) =>
        ResultPath?
            .Replace("{prefix}", prefix, StringComparison.Ordinal)
            .Replace("{name}", NameWithoutExtension(fileName), StringComparison.Ordinal);

    private static string NameWithoutExtension(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }
}
