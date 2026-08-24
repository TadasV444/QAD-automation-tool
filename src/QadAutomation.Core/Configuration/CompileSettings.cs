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
/// Compiling one report at a time through the Progress procedure editor.
/// </summary>
/// <remarks>
/// The manual procedure, which this mirrors exactly: run the editor, press F4,
/// type a <c>compile ... save into ...</c> statement, press F1. The keystrokes
/// live in the compiler rather than here because they are properties of the
/// Progress editor, not of a client's installation - a site that needed
/// different keys would need different code, not different config.
/// </remarks>
/// <param name="EditorCommand">
/// The full command that opens the editor, e.g.
/// <c>/appl/.../reports/compile_editor us test</c>. Held whole rather than
/// assembled from parts because its trailing argument names the environment and
/// the binary's own path may too. Config can be corrected without a rebuild; a
/// wrong guess in code cannot.
/// </param>
/// <param name="StatementTemplate">
/// The Progress statement to type, with <c>{remoteFile}</c> and
/// <c>{remoteDirectory}</c> substituted per file.
/// </param>
public sealed record EditorCompileSettings(string EditorCommand, string StatementTemplate)
{
    /// <summary>What every site observed so far types.</summary>
    public const string DefaultStatementTemplate = "compile {remoteFile} save into {remoteDirectory}.";

    /// <summary>The statement to type for one report.</summary>
    public string StatementFor(string remoteFile, string remoteDirectory) =>
        StatementTemplate
            .Replace("{remoteFile}", remoteFile, StringComparison.Ordinal)
            .Replace("{remoteDirectory}", remoteDirectory, StringComparison.Ordinal);
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
/// <param name="ManifestPath">
/// Remote file listing the programs to compile, one bare filename per line,
/// overwritten each run. Note it is a fixed shared path: two people compiling at
/// the same time would overwrite each other's list.
/// </param>
/// <param name="WorkingDirectory">Directory the commands are run from.</param>
/// <param name="CommandTemplate">
/// Run once per language with <c>{language}</c> substituted, e.g.
/// <c>./compile {language} test</c>. The environment is part of the template
/// rather than a separate field because it is the script's own argument and its
/// spelling is the site's, not this tool's.
/// </param>
/// <param name="Languages">
/// Language code to the root directory its compiled output lands under. The
/// results themselves sit one level deeper, in a folder named after the
/// program's prefix.
/// </param>
public sealed record ManifestCompileSettings(
    string ManifestPath,
    string WorkingDirectory,
    string CommandTemplate,
    IReadOnlyDictionary<string, string> Languages)
{
    /// <summary>The command to run for one language.</summary>
    public string CommandFor(string language) =>
        CommandTemplate.Replace("{language}", language, StringComparison.Ordinal);
}

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
