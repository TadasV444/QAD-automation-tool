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
/// <b>Not yet verified against a real server.</b> The shape comes from the
/// operator's description of the manual procedure; no SRC compile has been run
/// through this tool or watched closely enough to know what failure looks like.
/// It is modelled now so the config format does not have to change later, and
/// the compiler for it is deliberately unwritten.
/// </remarks>
/// <param name="ManifestPath">
/// Remote file listing the programs to compile, one name per line, overwritten
/// each run. Note it is a fixed shared path: two people compiling at the same
/// time would overwrite each other's list.
/// </param>
/// <param name="WorkingDirectory">Directory the commands are run from.</param>
/// <param name="Commands">
/// Run in order after the manifest is written, e.g. <c>./compile lt band</c>
/// then <c>./compile us band</c> - once per user language.
/// </param>
public sealed record SrcCompileSettings(
    string ManifestPath,
    string WorkingDirectory,
    IReadOnlyList<string> Commands);
