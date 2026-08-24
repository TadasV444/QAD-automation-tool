using QadAutomation.Core.Compile;
using QadAutomation.Core.Configuration;
using QadAutomation.Core.Tests.Transfer;
using QadAutomation.Core.Transfer;

namespace QadAutomation.Core.Tests.Compile;

/// <summary>
/// A shell that records what was typed into it and can be told to pretend a
/// compile succeeded.
/// </summary>
/// <remarks>
/// <para>
/// Like <c>FakeSftpServer</c>, this models the counterparty rather than
/// recording expectations - but it cannot go as far, because there is no
/// in-memory Progress editor to run. What it does model faithfully are the two
/// things that decide a verdict: a successful compile touches files on the
/// server, and a plain command answers when asked for its exit status. Set
/// <see cref="Server"/> and <see cref="Produces"/> and starting a compile really
/// does move the timestamps the compiler will read.
/// </para>
/// <para>
/// That keeps the tests honest about the part that matters. A test asserting
/// only "F1 was sent" would pass for a compiler that never checked whether
/// anything was built.
/// </para>
/// </remarks>
internal sealed class FakeSshShell : ISshShellFactory, ISshShell
{
    private readonly List<string> _sent = [];
    private int _goCount;
    private bool _awaitingExitCode;

    /// <summary>Everything sent, in order, including the key sequences.</summary>
    public IReadOnlyList<string> Sent => _sent;

    /// <summary>The whole session as one string, for substring assertions.</summary>
    public string Typed => string.Concat(_sent);

    public SshEndpoint? OpenedFor { get; private set; }

    public bool IsDisposed { get; private set; }

    /// <summary>What <see cref="ISshShell.ReadUntilIdle"/> returns.</summary>
    public string Screen { get; set; } = string.Empty;

    /// <summary>Set to throw when the shell is opened.</summary>
    public TransferException? OpenFailure { get; set; }

    /// <summary>
    /// What the shell reports when asked for the last command's exit status.
    /// </summary>
    /// <remarks>
    /// Null models a shell that never answers - the marker echo producing
    /// nothing recognisable - which is how a compiler that trusts an absent
    /// exit code gets caught.
    /// </remarks>
    public int? ExitCode { get; set; }

    /// <summary>The server a successful compile writes its <c>.r</c> to.</summary>
    public FakeSftpServer? Server { get; set; }

    /// <summary>
    /// What each F1 produces, in order: the remote paths it writes, or an empty
    /// set for a compile that fails and leaves the old files alone.
    /// </summary>
    /// <remarks>
    /// Per-press rather than one setting for the whole session, so a test can
    /// have the second of three reports fail - the case where a compiler could
    /// plausibly attribute the wrong screen to the wrong file. A <i>set</i> per
    /// press rather than a single path because one run of the SRC script builds
    /// the whole manifest at once.
    /// </remarks>
    public List<string[]> Produces { get; } = [];

    public ISshShell Open(SshEndpoint endpoint)
    {
        if (OpenFailure is not null)
        {
            throw OpenFailure;
        }

        OpenedFor = endpoint;
        return this;
    }

    public void Send(string text)
    {
        _sent.Add(text);

        if (text.Contains(ShellProtocol.ExitMarker, StringComparison.Ordinal))
        {
            _awaitingExitCode = true;
            return;
        }

        if (!IsCompileTrigger(text))
        {
            return;
        }

        var index = _goCount++;

        if (Server is null || index >= Produces.Count)
        {
            return;
        }

        foreach (var path in Produces[index])
        {
            Server.Touch(path);
        }
    }

    /// <summary>
    /// The two ways this site starts a compile: F1 in the editor, and running
    /// the batch script from the shell.
    /// </summary>
    /// <remarks>
    /// <c>cd</c> and the editor's own launch command are deliberately not
    /// triggers - counting them would shift every entry in
    /// <see cref="Produces"/> and make the tests pass for the wrong reason.
    /// </remarks>
    private static bool IsCompileTrigger(string text) =>
        text == ProgressKeys.Go || text.StartsWith("./", StringComparison.Ordinal);

    public string ReadUntilIdle(TimeSpan idleFor, TimeSpan timeout)
    {
        if (!_awaitingExitCode)
        {
            return Screen;
        }

        _awaitingExitCode = false;

        // Both occurrences a real shell produces: the echoed command line, then
        // its output. A compiler that read the first would report '$?' as the
        // status of every run.
        return ExitCode is { } code
            ? $"echo {ShellProtocol.ExitMarker}$?\n{ShellProtocol.ExitMarker}{code}\n"
            : $"echo {ShellProtocol.ExitMarker}$?\n";
    }

    public void Dispose() => IsDisposed = true;
}
