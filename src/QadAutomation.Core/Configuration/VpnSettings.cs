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

    /// <summary>
    /// FortiClient SSL VPN. Verified rather than dialled - the client exposes no
    /// connect command, so the operator opens the tunnel and the tool checks it.
    /// </summary>
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
/// <param name="Type">Which connector will handle this client.</param>
/// <param name="ConnectionName">The connection's name in whichever client owns it.</param>
/// <param name="Username">Only for types that authenticate through this tool.</param>
/// <param name="Password">As above; may be a <c>${VARIABLE}</c> resolved from .env.</param>
/// <param name="AdapterName">
/// Distinctive text from the tunnel's network adapter, for the types that verify
/// rather than dial. Optional: the defaults match the vendor's usual naming, and
/// this exists for the site whose adapter reads otherwise.
/// </param>
public sealed record VpnSettings(
    VpnType Type,
    string? ConnectionName,
    string? Username,
    string? Password,
    string? AdapterName = null);
