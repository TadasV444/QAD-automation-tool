using QadAutomation.Core.Configuration;
using QadAutomation.Core.Vpn;

namespace QadAutomation.Core.Tests.Vpn;

/// <summary>
/// FortiClient: verified, never dialled.
/// </summary>
public sealed class FortiClientVpnConnectorTests
{
    [Fact]
    public void A_tunnel_that_is_up_is_adopted_rather_than_opened()
    {
        // Nothing was opened, so nothing may be closed on dispose - the operator
        // may have connected it for something else entirely.
        var session = Connector("Fortinet SSL VPN Virtual Ethernet Adapter").Connect(Settings());

        Assert.False(session.OpenedByTool);
        Assert.Equal("Pilot-Tunnel", session.ConnectionName);
    }

    [Fact]
    public void The_adapter_is_matched_however_windows_capitalised_it()
    {
        Assert.True(Connector("FORTINET Virtual Adapter").IsConnected(Settings()));
    }

    [Fact]
    public void An_ordinary_machine_with_no_tunnel_is_not_connected()
    {
        // Guards the matcher against being too eager: none of Ethernet, Wi-Fi or
        // loopback may look like a tunnel.
        Assert.False(Connector().IsConnected(Settings()));
    }

    [Fact]
    public void A_missing_tunnel_names_it_and_says_to_connect_it_by_hand()
    {
        var message = Assert.Throws<VpnException>(() => Connector().Connect(Settings())).Message;

        Assert.Contains("Pilot-Tunnel", message, StringComparison.Ordinal);
        Assert.Contains("FortiClient", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_tunnel_lists_the_adapters_that_are_up()
    {
        // The other reason to land here is a tunnel that IS up under an
        // unrecognised name. Without the list there is no way to tell which
        // case it is, or what to put in 'adapterName'.
        var message = Assert.Throws<VpnException>(() => Connector().Connect(Settings())).Message;

        Assert.Contains("Wi-Fi", message, StringComparison.Ordinal);
        Assert.Contains("adapterName", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_configured_adapter_name_is_used_instead_of_the_defaults()
    {
        // For a site whose tunnel adapter is named after something other than
        // the vendor.
        var connector = Connector("PPP adapter Example-Tunnel");

        Assert.True(connector.IsConnected(Settings(adapterName: "Example-Tunnel")));
    }

    [Fact]
    public void A_configured_adapter_name_replaces_the_defaults_rather_than_adding_to_them()
    {
        // Otherwise a wrong 'adapterName' would be masked by a Fortinet adapter
        // belonging to some other tunnel, and the operator would never learn
        // their setting does nothing.
        var connector = Connector("Fortinet SSL VPN Virtual Ethernet Adapter");

        Assert.False(connector.IsConnected(Settings(adapterName: "Example-Tunnel")));
    }

    [Fact]
    public void One_vendors_two_tunnels_are_told_apart_by_their_adapters()
    {
        // Two clients on the same FortiClient install. Both descriptions
        // contain "Fortinet", so the defaults match either - meaning one
        // client's tunnel being up would satisfy the other's check, which is
        // the check passing in exactly the case it exists to catch.
        //
        // These two descriptions are real, from two live tunnels: an SSL one
        // and an IPsec one.
        var ssl = Connector("Fortinet SSL VPN Virtual Ethernet Adapter");

        Assert.True(ssl.IsConnected(Settings(adapterName: "SSL VPN")));
        Assert.False(ssl.IsConnected(Settings(adapterName: "NDIS")));
    }

    [Fact]
    public void The_other_clients_tunnel_does_not_satisfy_this_ones_check()
    {
        var ipsec = Connector("Fortinet Virtual Ethernet Adapter (NDIS 6.30) #2");

        Assert.True(ipsec.IsConnected(Settings(adapterName: "NDIS")));
        Assert.False(ipsec.IsConnected(Settings(adapterName: "SSL VPN")));

        // And the default would have matched it, which is the whole reason
        // both clients now name their adapter.
        Assert.True(ipsec.IsConnected(Settings()));
    }

    [Fact]
    public void Disconnect_refuses_rather_than_pretending_to_have_worked()
    {
        // Reporting success for a tunnel still carrying traffic is the failure
        // worth avoiding - the operator would believe they were off the
        // client's network.
        var connector = Connector("Fortinet SSL VPN Virtual Ethernet Adapter");

        Assert.Contains(
            "FortiClient",
            Assert.Throws<VpnException>(() => connector.Disconnect(Settings())).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Disposing_an_adopted_session_leaves_the_tunnel_alone()
    {
        var connector = Connector("Fortinet SSL VPN Virtual Ethernet Adapter");
        var settings = Settings();

        using (connector.Connect(settings))
        {
            // Nothing here: the point is what dispose does not do.
        }

        Assert.True(connector.IsConnected(settings));
    }

    private static FortiClientVpnConnector Connector(params string[] extraAdapters) =>
        new(new FakeNetworkInterfaces().With(extraAdapters));

    private static VpnSettings Settings(string? adapterName = null) =>
        new(VpnType.FortiClient, "Pilot-Tunnel", "someone", null, adapterName);
}
