using QadAutomation.Core.Configuration;

namespace QadAutomation.Core.Vpn;

/// <summary>
/// Brings a client's VPN up and down.
/// </summary>
/// <remarks>
/// <para>
/// The interface exists so the rest of the tool can depend on "a VPN can be
/// established" without depending on <i>how</i>. Some sites use the Windows
/// built-in client, driven by <c>rasdial</c>; others use FortiClient, which has
/// no comparable command-line entry point and will need a different
/// implementation entirely. Neither knows about the other, and adding a third
/// changes no existing file - the open/closed principle where it actually earns
/// something, because a new VPN vendor is a genuinely expected change.
/// </para>
/// <para>
/// The settings are passed per call rather than injected into the constructor so
/// that one connector instance can serve every client. A connector is a piece of
/// behaviour, not a piece of client data.
/// </para>
/// </remarks>
public interface IVpnConnector
{
    /// <summary>
    /// Whether the connection described by <paramref name="settings"/> is up.
    /// </summary>
    /// <exception cref="VpnException">If the state cannot be determined.</exception>
    bool IsConnected(VpnSettings settings);

    /// <summary>
    /// Ensures the connection is up, returning a session that will restore the
    /// previous state when disposed.
    /// </summary>
    /// <remarks>
    /// Idempotent: calling it while already connected succeeds and returns a
    /// session that will not disconnect on dispose.
    /// </remarks>
    /// <exception cref="VpnException">If the connection cannot be established.</exception>
    IVpnSession Connect(VpnSettings settings);

    /// <summary>
    /// Takes the connection down, whoever brought it up.
    /// </summary>
    /// <remarks>
    /// The deliberate exception to the "only close what we opened" rule, because
    /// this one is reached only by an operator typing <c>qad vpn disconnect</c> -
    /// an explicit instruction rather than a side effect. Succeeds quietly if the
    /// connection is already down.
    /// </remarks>
    /// <exception cref="VpnException">If the disconnect fails.</exception>
    void Disconnect(VpnSettings settings);
}
