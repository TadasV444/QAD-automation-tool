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
/// in-memory Progress editor to run. What it does model faithfully is the one
/// thing that matters: a successful compile touches the <c>.r</c> on the server.
/// Point <see cref="CompilesInto"/> at a <c>FakeSftpServer</c> and typing the
/// statement really does move the timestamp the compiler will read.
/// </para>
/// <para>
/// That keeps the tests honest about the part that decides the verdict. A test
/// asserting only "F1 was sent" would pass for a compiler that never checked
/// whether anything was built.
/// </para>
/// </remarks>
internal sealed class FakeSshShell : ISshShellFactory, ISshShell
{
    private readonly List<string> _sent = [];
    private int _goCount;

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

    public string ReadUntilIdle(TimeSpan idleFor, TimeSpan timeout) => Screen;

    public void Dispose() => IsDisposed = true;
}
