using System.Net.NetworkInformation;

namespace QadAutomation.Core.Vpn;

/// <summary>
/// The machine's network adapters that are currently up.
/// </summary>
/// <remarks>
/// <para>
/// A seam for the same reason as <c>IProcessRunner</c>: the real thing depends on
/// a VPN tunnel existing on the machine running the test, which no build server
/// can provide. Behind the interface, "the tunnel is up" becomes a value a test
/// can supply.
/// </para>
/// <para>
/// Deliberately just names. Everything else an adapter exposes - addresses,
/// speeds, statistics - would invite checks that look more thorough while being
/// no more reliable at answering the only question asked: is the tunnel there.
/// </para>
/// </remarks>
public interface INetworkInterfaces
{
    /// <summary>
    /// Names and descriptions of every adapter currently operational.
    /// </summary>
    /// <remarks>
    /// Both, because a tunnel is identifiable by either and sites differ over
    /// which one carries the recognisable text: Windows names the adapter after
    /// the tunnel in some installs and after the vendor's driver in others.
    /// </remarks>
    IReadOnlyList<string> Active();
}

/// <inheritdoc cref="INetworkInterfaces" />
public sealed class NetworkInterfaces : INetworkInterfaces
{
    /// <inheritdoc />
    public IReadOnlyList<string> Active() =>
        [.. NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .SelectMany(adapter => new[] { adapter.Name, adapter.Description })
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
}
