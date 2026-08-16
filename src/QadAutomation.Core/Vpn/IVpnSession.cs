namespace QadAutomation.Core.Vpn;

/// <summary>
/// A VPN connection that is up, and the record of who brought it up.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a session type exists at all.</b> A pair of <c>Connect</c> /
/// <c>Disconnect</c> calls would be simpler, and wrong. The upload and compile
/// steps will fail sometimes - that is the normal case, not the exceptional one -
/// and every failure path would need its own <c>Disconnect</c>. One forgotten
/// path leaves the operator silently on a client's network. Tying teardown to
/// <see cref="IDisposable"/> means the compiler's <c>using</c> handles the paths
/// nobody thought about.
/// </para>
/// <para>
/// <b>Why <see cref="OpenedByTool"/> is part of the contract.</b> Disposing must
/// only undo what this run actually did. An operator who was already connected -
/// perhaps with the client on the phone, mid-investigation - must not have that
/// connection dropped as a side effect of running a compile. Restoring the prior
/// state is the goal, not reaching a fixed one.
/// </para>
/// </remarks>
public interface IVpnSession : IDisposable
{
    /// <summary>The connection this session refers to, for messages.</summary>
    string ConnectionName { get; }

    /// <summary>
    /// <c>true</c> if this run established the connection, <c>false</c> if it was
    /// already up. Only a connection we opened is ours to close.
    /// </summary>
    bool OpenedByTool { get; }

    /// <summary>
    /// Suppresses the disconnect on dispose, leaving the connection up.
    /// </summary>
    /// <remarks>
    /// For <c>qad vpn connect</c>, whose entire purpose is to leave the VPN
    /// running after the process exits. Expressed as an explicit opt-out so that
    /// the safe behaviour stays the default and staying connected is always a
    /// deliberate choice at the call site.
    /// </remarks>
    void KeepOpen();
}
