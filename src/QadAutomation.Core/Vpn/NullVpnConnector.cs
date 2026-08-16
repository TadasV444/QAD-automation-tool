using QadAutomation.Core.Configuration;

namespace QadAutomation.Core.Vpn;

/// <summary>
/// The connector for <see cref="VpnType.None"/>: the operator connects by hand.
/// </summary>
/// <remarks>
/// <para>
/// A real implementation of the null object pattern rather than a placeholder.
/// Without it, every caller would need <c>if (client.Vpn.Type != VpnType.None)</c>
/// around its VPN handling - the branch would be repeated at each call site and
/// eventually forgotten at one of them.
/// </para>
/// <para>
/// It reports "connected" because from the caller's point of view that is the
/// truth being asserted: nothing is blocking the upload. Whether the operator has
/// actually connected is discovered a moment later, by the SSH connection
/// failing - a far more reliable check than anything this class could perform,
/// and one that has to work anyway.
/// </para>
/// </remarks>
public sealed class NullVpnConnector : IVpnConnector
{
    private const string Description = "not managed by this tool";

    /// <inheritdoc />
    public bool IsConnected(VpnSettings settings) => true;

    /// <inheritdoc />
    public IVpnSession Connect(VpnSettings settings) => VpnSession.Adopted(Description);

    /// <inheritdoc />
    public void Disconnect(VpnSettings settings)
    {
        // Nothing was opened here, so there is nothing to close.
    }
}
