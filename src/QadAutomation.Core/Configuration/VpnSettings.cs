namespace QadAutomation.Core.Configuration;

/// <summary>
/// How a client's network is reached.
/// </summary>
public enum VpnType
{
    /// <summary>
    /// No VPN is established by the tool. The operator is expected to already be
    /// on the client's network; the tool only verifies reachability.
    /// </summary>
    None,

    /// <summary>Windows built-in VPN, driven by <c>rasdial</c>.</summary>
    WindowsRas,

    /// <summary>FortiClient.</summary>
    FortiClient
}

/// <summary>
/// A client's VPN configuration, already validated.
/// </summary>
/// <remarks>
/// Deliberately data-only. Knowing <i>how</i> to bring a VPN up is the job of an
/// <c>IVpnConnector</c> implementation selected from <see cref="Type"/>; this
/// record only carries the parameters that implementation will need. Keeping the
/// data separate from the behaviour is what allows a new VPN type to be added
/// without touching any existing connector.
/// </remarks>
public sealed record VpnSettings(
    VpnType Type,
    string? ConnectionName,
    string? Username,
    string? Password);
