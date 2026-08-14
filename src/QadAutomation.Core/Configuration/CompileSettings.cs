namespace QadAutomation.Core.Configuration;

/// <summary>
/// How a compile is driven on the remote host.
/// </summary>
/// <remarks>
/// This discriminator is what an <c>ICompiler</c> factory will switch on. The two
/// strategies are genuinely different interaction models, not just different
/// command strings, which is why they are separate strategies rather than one
/// parameterised implementation.
/// </remarks>
public enum CompileStrategy
{
    /// <summary>
    /// One or more self-contained shell commands. Output is streamed back
    /// verbatim; nothing needs to be read in order to decide what to send next.
    /// </summary>
    DirectCommand,

    /// <summary>
    /// A menu-driven interactive CLI. Requires an interactive shell and
    /// expect-style prompt matching, because the next keystroke depends on what
    /// the program just printed.
    /// </summary>
    InteractiveMenu
}

/// <summary>
/// The compile recipe for one environment.
/// </summary>
/// <param name="Strategy">Selects the <c>ICompiler</c> implementation.</param>
/// <param name="Commands">
/// Command templates, run in order. <c>{remotePath}</c>, <c>{fileName}</c> and
/// <c>{remoteFile}</c> placeholders are substituted per file at execution time.
/// </param>
public sealed record CompileSettings(
    CompileStrategy Strategy,
    IReadOnlyList<string> Commands);
