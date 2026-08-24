using System.Text.RegularExpressions;
using QadAutomation.Core.Configuration;

namespace QadAutomation.Core.Compile;

/// <summary>
/// An interactive shell on the remote host, with a terminal attached.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not just "run a command".</b> The QRF compile goes through a
/// character-mode Progress editor: it draws a screen, waits for function keys,
/// and never exits on its own. There is no command line to hand it and no exit
/// status to collect. Something has to hold a terminal open and press keys.
/// </para>
/// <para>
/// <b>Why there is no prompt matching.</b> The obvious design is expect-style -
/// wait for a known string, then send the next thing. It was rejected because
/// the editor's screen is redrawn with cursor-positioning escape sequences, so
/// the "prompt" is not reliably a substring of anything, and a pattern that
/// fails to match hangs rather than fails. <see cref="ReadUntilIdle"/> waits for
/// the output to stop instead, which needs to know nothing about what is on the
/// screen.
/// </para>
/// <para>
/// Nothing read here is used to decide success. The screen is captured only so
/// it can be shown to the operator when a compile fails; the verdict comes from
/// the <c>.r</c> timestamp over SFTP. That split is deliberate - screen-scraping
/// a Progress error message would be guesswork, and a wrong guess in the
/// optimistic direction reports a deploy that did not happen.
/// </para>
/// </remarks>
public interface ISshShell : IDisposable
{
    /// <summary>Sends raw text, exactly as typed. No newline is added.</summary>
    void Send(string text);

    /// <summary>
    /// Reads until nothing has arrived for <paramref name="idleFor"/>, or
    /// <paramref name="timeout"/> elapses overall.
    /// </summary>
    /// <remarks>
    /// For the Progress editor, which draws a screen and then waits: silence is
    /// the only signal it gives. Do not use it to wait for a <i>command</i> to
    /// finish - see <see cref="ReadUntil"/> for why.
    /// </remarks>
    /// <returns>Everything received during the wait, escape sequences included.</returns>
    string ReadUntilIdle(TimeSpan idleFor, TimeSpan timeout);

    /// <summary>
    /// Reads until <paramref name="marker"/> matches what has arrived so far, or
    /// <paramref name="timeout"/> elapses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The right way to wait for a command. Treating a pause as completion is
    /// wrong for anything that does real work: a build that stops printing for
    /// three seconds mid-compile looks finished, and whatever is sent next
    /// arrives as input to a program still running.
    /// </para>
    /// <para>
    /// That is not hypothetical - it is exactly how the first real run against
    /// a build script failed, and it failed in the direction of reporting a
    /// working compile as broken.
    /// </para>
    /// </remarks>
    /// <returns>
    /// Everything received, including the marker. On timeout, whatever arrived -
    /// so the caller can show it rather than only reporting that time ran out.
    /// </returns>
    string ReadUntil(Regex marker, TimeSpan timeout);
}

/// <summary>Opens <see cref="ISshShell"/>s.</summary>
public interface ISshShellFactory
{
    /// <exception cref="Transfer.TransferException">
    /// If the host is unreachable or the credentials are refused.
    /// </exception>
    ISshShell Open(SshEndpoint endpoint);
}

/// <summary>
/// How the tool asks a plain shell for the last command's exit status.
/// </summary>
/// <remarks>
/// <para>
/// A shell stream carries no out-of-band result, so the only way to learn an
/// exit code is to make the shell print it. Echoing
/// <c>{marker}$?</c> immediately after the command does that.
/// </para>
/// <para>
/// The marker is deliberately unlike anything a build script would print. A bare
/// number could not be told apart from the script's own output, and matching the
/// wrong line would invent a verdict rather than fail to find one.
/// </para>
/// </remarks>
public static class ShellProtocol
{
    /// <summary>Prefixes the echoed exit code.</summary>
    public const string ExitMarker = "__QAD_EXIT__";

    /// <summary>
    /// Runs <paramref name="command"/> and then reports its status, as one line.
    /// </summary>
    /// <remarks>
    /// One line, not two, because the marker is also how the tool knows the
    /// command has finished. Sent separately, the second line would have to be
    /// typed while the first might still be running.
    /// </remarks>
    public static string WithExitCode(string command) => $"{command}; echo {ExitMarker}$?";

    /// <summary>
    /// Matches the echoed status, and only that.
    /// </summary>
    /// <remarks>
    /// The digit is what matters. A terminal echoes the command line back
    /// before running it, so the marker appears twice - once followed by a
    /// literal <c>$?</c>, and once by the number. Waiting on the marker alone
    /// would stop at the echo and read a result for a command that had not yet
    /// started.
    /// </remarks>
    public static readonly Regex CompletedExitCode =
        new(ExitMarker + @"(\d+)", RegexOptions.Compiled);
}

/// <summary>
/// The keystrokes the Progress procedure editor responds to.
/// </summary>
/// <remarks>
/// <para>
/// Constants rather than configuration. These are properties of the Progress
/// editor and of the terminal type we ask for, not of any client's installation
/// - a site needing different keys would need different code, and pretending
/// otherwise would put an operator in the position of guessing escape sequences
/// in a JSON file.
/// </para>
/// <para>
/// The codes are the xterm SS3 forms, which match the terminal type
/// <c>SshNetShell</c> requests. Both are here, next to each other, because
/// changing one without the other silently sends keys the editor will ignore -
/// and an ignored keystroke looks exactly like a compile that did nothing.
/// </para>
/// </remarks>
public static class ProgressKeys
{
    /// <summary>Terminal type requested when opening the shell.</summary>
    public const string TerminalType = "xterm";

    /// <summary>
    /// ESC, written as a code point rather than typed into a string literal.
    /// A raw escape byte in source is invisible in every editor and diff, and
    /// the one thing worse than a wrong keystroke here is an unreviewable one.
    /// </summary>
    private const char Escape = (char)27;

    /// <summary>F1 - runs the buffer, "GO".</summary>
    public static readonly string Go = Escape + "OP";

    /// <summary>F4 - clears the buffer, ready for the next statement.</summary>
    public static readonly string NewBuffer = Escape + "OS";

    /// <summary>Carriage return, as a terminal sends it.</summary>
    public const string Enter = "\r";
}
