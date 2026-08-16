using QadAutomation.Core.Configuration;

namespace QadAutomation.Core.Vpn;

/// <summary>
/// Picks the connector that matches a client's <see cref="VpnType"/>.
/// </summary>
/// <remarks>
/// The one place in the tool that switches on <see cref="VpnType"/>. Everything
/// downstream holds an <see cref="IVpnConnector"/> and cannot tell a
/// FortiClient site from a Windows RAS one - so adding a vendor means adding a
/// class and one arm here, and touching nothing else.
/// </remarks>
public interface IVpnConnectorFactory
{
    /// <summary>
    /// Returns the connector for <paramref name="settings"/>.
    /// </summary>
    /// <exception cref="VpnException">
    /// If the VPN type is recognised by the configuration but not yet automated.
    /// </exception>
    IVpnConnector Create(VpnSettings settings);
}
