namespace QadAutomation.Core.Configuration;

/// <summary>
/// How each kind of program is compiled on the remote host.
/// </summary>
/// <remarks>
/// <para>
/// This replaced a single <c>strategy</c> plus a flat list of command strings.
/// That shape was written before either procedure had been observed, and it
/// could not express either of them: QRF is one interactive editor session per
/// file, SRC is a remote manifest file followed by a batch script run once for
/// the whole set. They share a server and nothing else.
/// </para>
/// <para>
/// So there is no strategy discriminator any more. The two recipes have
/// different shapes, and the shape is the discriminator - a config that names
/// <c>qrf</c> cannot accidentally be read as an SRC recipe, which a mistyped
/// enum value could have allowed.
/// </para>
/// <para>
/// Both halves are optional. An environment with no <c>src</c> block simply
/// cannot compile SRC yet, and says so when asked; requiring a recipe that has
/// never been run against a real server would only invite an invented one.
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
/// Compiling one QRF report through the Progress procedure editor.
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
/// assembled from parts because the second argument names the environment and it
/// is not yet established whether the binary's own path follows it. Config can
/// be corrected without a rebuild; a wrong guess in code cannot.
/// </param>
/// <param name="StatementTemplate">
/// The Progress statement to type, with <c>{remoteFile}</c> and
/// <c>{remoteDirectory}</c> substituted per file.
/// </param>
public sealed record QrfCompileSettings(string EditorCommand, string StatementTemplate)
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
/// Compiling SRC programs through the batch compile script.
/// </summary>
/// <remarks>
/// <para>
/// Nothing like the QRF procedure. The programs to build are listed in a file on
/// the server, and then one command per user language compiles the whole list -
/// so this is per-batch where QRF is per-file, and it has a step, writing the
/// manifest, that QRF has no equivalent of.
/// </para>
/// <para>
/// A single SRC program produces <b>two</b> compiled results, one per language,
/// in different directory trees. That is why the languages are a map rather than
/// a list of command strings: each entry supplies both the command to run and
/// the place to check afterwards, so the two cannot drift apart. Kept as two
/// separate settings they could, and the failure mode would be a compile
/// reported against a directory nothing writes to.
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
/// spelling (<c>test</c>, <c>euro</c>) is the site's, not this tool's.
/// </param>
/// <param name="Languages">
/// Language code to the root directory its compiled output lands under. The
/// results themselves sit one level deeper, in a folder named after the
/// program's prefix.
/// </param>
public sealed record SrcCompileSettings(
    string ManifestPath,
    string WorkingDirectory,
    string CommandTemplate,
    IReadOnlyDictionary<string, string> Languages)
{
    /// <summary>The command to run for one language.</summary>
    public string CommandFor(string language) =>
        CommandTemplate.Replace("{language}", language, StringComparison.Ordinal);
}
