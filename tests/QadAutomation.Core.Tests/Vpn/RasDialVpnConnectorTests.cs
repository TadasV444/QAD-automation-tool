using QadAutomation.Core.Configuration;
using QadAutomation.Core.Vpn;

namespace QadAutomation.Core.Tests.Vpn;

public sealed class RasDialVpnConnectorTests
{
    private const string ConnectionName = "PilotVpn";

    private readonly FakeRasDial _rasDial = new();

    [Fact]
    public void A_connection_that_is_down_is_reported_as_down()
    {
        Assert.False(Connector().IsConnected(Settings()));
    }

    [Fact]
    public void A_connection_that_is_up_is_reported_as_up()
    {
        _rasDial.SetAlreadyConnected(ConnectionName);

        Assert.True(Connector().IsConnected(Settings()));
    }

    [Fact]
    public void Connecting_brings_the_connection_up()
    {
        using var session = Connector().Connect(Settings());

        Assert.True(_rasDial.IsUp(ConnectionName));
        Assert.True(session.OpenedByTool);
    }

    [Fact]
    public void Saved_windows_credentials_are_used_when_none_are_configured()
    {
        // The recommended setup: the Windows VPN entry remembers the credentials,
        // so no VPN password exists in .env for anything to leak.
        using var session = Connector().Connect(Settings());

        Assert.Equal([ConnectionName], Assert.Single(_rasDial.ConnectCalls));
    }

    [Fact]
    public void Configured_credentials_are_passed_when_they_are_supplied()
    {
        using var session = Connector().Connect(Settings(username: "pilot-user", password: "pw"));

        Assert.Equal([ConnectionName, "pilot-user", "pw"], Assert.Single(_rasDial.ConnectCalls));
    }

    [Fact]
    public void A_username_without_a_password_is_rejected_before_anything_runs()
    {
        // rasdial would prompt, and with stdin closed it would fail obscurely.
        var message = Assert.Throws<VpnException>(
            () => Connector().Connect(Settings(username: "pilot-user"))).Message;

        Assert.Contains("Set both, or set neither", message);
        Assert.Empty(_rasDial.ConnectCalls);
    }

    [Fact]
    public void Connecting_when_already_connected_changes_nothing()
    {
        _rasDial.SetAlreadyConnected(ConnectionName);

        using var session = Connector().Connect(Settings());

        Assert.False(session.OpenedByTool);
        Assert.Empty(_rasDial.ConnectCalls);
    }

    [Fact]
    public void Disposing_a_session_we_opened_disconnects()
    {
        using (var session = Connector().Connect(Settings()))
        {
            Assert.True(_rasDial.IsUp(ConnectionName));
        }

        Assert.False(_rasDial.IsUp(ConnectionName));
    }

    [Fact]
    public void Disposing_a_session_we_did_not_open_leaves_the_connection_alone()
    {
        // The safety property that matters most: an operator already connected -
        // perhaps mid-investigation with the client - must not have the
        // connection dropped underneath them by a compile that happened to run.
        _rasDial.SetAlreadyConnected(ConnectionName);

        using (var session = Connector().Connect(Settings()))
        {
        }

        Assert.True(_rasDial.IsUp(ConnectionName));
        Assert.Empty(_rasDial.DisconnectCalls);
    }

    [Fact]
    public void Keeping_a_session_open_suppresses_the_disconnect()
    {
        using (var session = Connector().Connect(Settings()))
        {
            session.KeepOpen();
        }

        Assert.True(_rasDial.IsUp(ConnectionName));
    }

    [Fact]
    public void Disconnecting_takes_the_connection_down()
    {
        _rasDial.SetAlreadyConnected(ConnectionName);

        Connector().Disconnect(Settings());

        Assert.False(_rasDial.IsUp(ConnectionName));
    }

    [Fact]
    public void Disconnecting_something_already_down_is_a_quiet_no_op()
    {
        Connector().Disconnect(Settings());

        Assert.Empty(_rasDial.DisconnectCalls);
    }

    [Fact]
    public void A_rejected_password_says_so_instead_of_printing_a_number()
    {
        _rasDial.ConnectFailureCode = 691;

        var message = Assert.Throws<VpnException>(() => Connector().Connect(Settings())).Message;

        Assert.Contains("username or password was rejected", message);
        Assert.Contains(ConnectionName, message);
    }

    [Fact]
    public void An_unknown_connection_name_points_at_the_command_that_lists_them()
    {
        _rasDial.ConnectFailureCode = 623;

        var message = Assert.Throws<VpnException>(() => Connector().Connect(Settings())).Message;

        Assert.Contains("Get-VpnConnection", message);
    }

    [Fact]
    public void A_blocked_network_is_distinguished_from_a_bad_password()
    {
        _rasDial.ConnectFailureCode = 809;

        var message = Assert.Throws<VpnException>(() => Connector().Connect(Settings())).Message;

        Assert.Contains("blocking the VPN", message);
        Assert.DoesNotContain("password was rejected", message);
    }

    [Fact]
    public void An_unmapped_error_code_still_reports_the_number()
    {
        _rasDial.ConnectFailureCode = 31337;

        Assert.Contains("31337", Assert.Throws<VpnException>(() => Connector().Connect(Settings())).Message);
    }

    [Fact]
    public void A_failure_never_echoes_the_password()
    {
        _rasDial.ConnectFailureCode = 691;

        var message = Assert.Throws<VpnException>(
            () => Connector().Connect(Settings(username: "pilot-user", password: "hunter2"))).Message;

        Assert.DoesNotContain("hunter2", message);
    }

    [Fact]
    public void A_missing_connection_name_is_caught_before_anything_runs()
    {
        var settings = new VpnSettings(VpnType.WindowsRas, ConnectionName: null, null, null);

        var message = Assert.Throws<VpnException>(() => Connector().Connect(settings)).Message;

        Assert.Contains("connectionName", message);
        Assert.Empty(_rasDial.Calls);
    }

    [Fact]
    public void Rasdial_not_being_runnable_is_a_vpn_error_not_a_crash()
    {
        var connector = new RasDialVpnConnector(new UnstartableProcessRunner(), "rasdial.exe");

        // Must surface as VpnException so the operator gets the message and exit
        // code 4, rather than a stack trace and exit code 99.
        Assert.Throws<VpnException>(() => connector.Connect(Settings()));
    }

    // --- helpers ---------------------------------------------------------

    private RasDialVpnConnector Connector() => new(_rasDial, "rasdial.exe");

    private static VpnSettings Settings(string? username = null, string? password = null) =>
        new(VpnType.WindowsRas, ConnectionName, username, password);
}

public sealed class NullVpnConnectorTests
{
    private static readonly VpnSettings Settings = new(VpnType.None, null, null, null);

    [Fact]
    public void It_reports_connected_so_callers_need_no_special_case()
    {
        Assert.True(new NullVpnConnector().IsConnected(Settings));
    }

    [Fact]
    public void Its_session_never_claims_to_have_opened_anything()
    {
        using var session = new NullVpnConnector().Connect(Settings);

        Assert.False(session.OpenedByTool);
    }
}

public sealed class VpnConnectorFactoryTests
{
    private readonly VpnConnectorFactory _factory = new(new FakeRasDial());

    [Fact]
    public void None_gets_the_null_connector()
    {
        Assert.IsType<NullVpnConnector>(_factory.Create(new VpnSettings(VpnType.None, null, null, null)));
    }

    [Fact]
    public void WindowsRas_gets_the_rasdial_connector()
    {
        Assert.IsType<RasDialVpnConnector>(
            _factory.Create(new VpnSettings(VpnType.WindowsRas, "x", null, null)));
    }

    [Fact]
    public void FortiClient_fails_with_advice_rather_than_a_null_reference()
    {
        var message = Assert.Throws<VpnException>(
            () => _factory.Create(new VpnSettings(VpnType.FortiClient, null, null, null))).Message;

        Assert.Contains("Connect it by hand", message);
    }
}
