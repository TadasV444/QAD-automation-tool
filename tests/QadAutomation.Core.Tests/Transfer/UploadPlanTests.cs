using QadAutomation.Core;
using QadAutomation.Core.Configuration;
using QadAutomation.Core.Tickets;
using QadAutomation.Core.Transfer;

namespace QadAutomation.Core.Tests.Transfer;

/// <summary>
/// Routing. No server, no VPN, no files - the decision is a pure function, so
/// every case is cheap to pin down.
/// </summary>
public sealed class UploadPlanTests
{
    private const string SrcPath = "/appl/global/xrc";
    private const string QrfPath = "/appl/desktop/test/reports";

    [Fact]
    public void A_src_file_is_routed_to_the_src_path()
    {
        var plan = Plan(Src("a.p"));

        Assert.Equal($"{SrcPath}/a.p", Assert.Single(plan.Uploads).RemotePath);
    }

    [Fact]
    public void A_qrf_file_is_routed_to_the_qrf_path()
    {
        var plan = Plan(Qrf("b.p"));

        Assert.Equal($"{QrfPath}/b.p", Assert.Single(plan.Uploads).RemotePath);
    }

    [Fact]
    public void A_mixed_ticket_sends_each_kind_to_its_own_destination()
    {
        // The accident this whole tool exists to prevent: a SRC program landing
        // in the QRF directory. Nothing about it announces itself at runtime.
        var plan = Plan(Src("a.p"), Qrf("b.p"), Src("c.p"));

        Assert.Equal($"{SrcPath}/a.p", plan.Uploads[0].RemotePath);
        Assert.Equal($"{QrfPath}/b.p", plan.Uploads[1].RemotePath);
        Assert.Equal($"{SrcPath}/c.p", plan.Uploads[2].RemotePath);
    }

    [Fact]
    public void Remote_paths_use_forward_slashes_even_though_we_are_on_windows()
    {
        // Path.Combine here would produce a backslash and create a file on the
        // server literally named "reports\b.p".
        Assert.DoesNotContain('\\', Plan(Qrf("b.p")).Uploads[0].RemotePath);
    }

    [Fact]
    public void A_trailing_slash_on_the_configured_path_does_not_double_up()
    {
        var environment = Environment(qrf: QrfPath + "/");

        var plan = UploadPlan.Create(Ticket(Qrf("b.p")), environment, "pilot");

        Assert.Equal($"{QrfPath}/b.p", plan.Uploads[0].RemotePath);
    }

    [Fact]
    public void A_kind_with_no_configured_path_fails_before_anything_connects()
    {
        // Better than skipping the file silently or inventing a destination.
        var environment = Environment(qrf: null);

        var message = Assert.Throws<ConfigurationException>(
            () => UploadPlan.Create(Ticket(Qrf("b.p")), environment, "pilot")).Message;

        Assert.Contains("qrfRemotePath", message);
        Assert.Contains("pilot", message);
    }

    [Fact]
    public void A_missing_path_for_a_kind_that_is_not_present_is_not_an_error()
    {
        // A client that only ever deploys QRF should not have to invent a SRC path.
        var environment = Environment(src: null);

        Assert.Single(UploadPlan.Create(Ticket(Qrf("b.p")), environment, "pilot").Uploads);
    }

    [Fact]
    public void An_empty_ticket_produces_an_empty_plan_rather_than_an_error()
    {
        Assert.True(Plan().IsEmpty);
    }

    [Fact]
    public void Destinations_are_deduplicated()
    {
        Assert.Equal([QrfPath, SrcPath], Plan(Src("a.p"), Src("c.p"), Qrf("b.p")).Destinations);
    }

    [Fact]
    public void The_plan_knows_when_it_targets_production()
    {
        var environment = Environment(isProduction: true);

        Assert.True(UploadPlan.Create(Ticket(Src("a.p")), environment, "pilot").IsProduction);
    }

    // --- helpers ---------------------------------------------------------

    private static ProgramFile Src(string name) => new(ProgramKind.Src, @"C:\tickets\T1\SRC\" + name);

    private static ProgramFile Qrf(string name) => new(ProgramKind.Qrf, @"C:\tickets\T1\QRF\" + name);

    private static TicketFolder Ticket(params ProgramFile[] files) =>
        new("Ticket 9999555", @"C:\tickets\T1", files);

    private static UploadPlan Plan(params ProgramFile[] files) =>
        UploadPlan.Create(Ticket(files), Environment(), "pilot");

    private static QadEnvironment Environment(
        string? src = SrcPath,
        string? qrf = QrfPath,
        bool isProduction = false) =>
        new(
            "TEST",
            isProduction,
            new SshEndpoint("qad.example", 22, "mfg", "pw", null),
            new RemotePaths(src, qrf),
            new CompileSettings(CompileStrategy.InteractiveMenu, []));
}
