using QadAutomation.Core.Compile;
using QadAutomation.Core.Configuration;
using QadAutomation.Core.Tickets;

namespace QadAutomation.Core.Tests.Compile;

/// <summary>
/// Planning a compile: pure, no server, no VPN.
/// </summary>
public sealed class CompilePlanTests
{
    private const string QrfPath = "/appl/desktop/test/reports";
    private const string SrcPath = "/appl/global/xrc";

    [Fact]
    public void A_qrf_report_is_compiled_into_its_own_directory()
    {
        var compile = Assert.Single(Plan(Qrf("rep.p")).Using<PlannedEditorCompile>());

        Assert.Equal($"{QrfPath}/rep.p", compile.RemoteFile);
        Assert.Equal(
            $"compile {QrfPath}/rep.p save into {QrfPath}.",
            compile.Statement);
    }

    [Fact]
    public void The_expected_result_file_is_the_source_with_an_r_extension()
    {
        // This is the whole verification mechanism: get it wrong and every
        // compile reports failure, or worse, checks a file nobody is writing.
        var compile = Assert.Single(Plan(Qrf("rep.p")).Using<PlannedEditorCompile>());

        Assert.Equal($"{QrfPath}/rep.r", compile.RemoteResult);
    }

    [Fact]
    public void A_dot_in_a_directory_name_is_not_mistaken_for_an_extension()
    {
        var environment = Environment(qrf: "/appl/v8.2/reports");

        var compile = Assert.Single(
            CompilePlan.Create(Ticket(Qrf("noext")), environment, "pilot").Using<PlannedEditorCompile>());

        Assert.Equal("/appl/v8.2/reports/noext.r", compile.RemoteResult);
    }

    [Fact]
    public void A_trailing_slash_on_the_configured_path_does_not_double_up()
    {
        var environment = Environment(qrf: QrfPath + "/");

        var compile = Assert.Single(
            CompilePlan.Create(Ticket(Qrf("rep.p")), environment, "pilot").Using<PlannedEditorCompile>());

        Assert.Equal($"{QrfPath}/rep.p", compile.RemoteFile);
        Assert.DoesNotContain("//", compile.Statement, StringComparison.Ordinal);
    }

    [Fact]
    public void A_src_program_is_skipped_with_a_reason_when_the_environment_cannot_build_it()
    {
        // The dangerous outcome is not an error - it is an operator believing a
        // ticket is deployed when half of it was never built. This environment
        // is configured for QRF only.
        var plan = Plan(Qrf("rep.p"), Src("xxprog.p"));

        Assert.Single(plan.Using<PlannedEditorCompile>());
        Assert.Empty(plan.Using<PlannedManifestCompile>());

        var skipped = Assert.Single(plan.Skipped);

        Assert.Equal("xxprog.p", skipped.File.FileName);
        Assert.Contains("compile.src", skipped.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_qrf_recipe_skips_rather_than_throwing()
    {
        // Unlike an upload with no destination, this is a normal state: the
        // recipe simply has not been worked out for that environment yet.
        var plan = CompilePlan.Create(Ticket(Qrf("rep.p")), EnvironmentWithoutRecipe(), "pilot");

        Assert.True(plan.IsEmpty);
        Assert.Contains("compile.qrf", Assert.Single(plan.Skipped).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_qrf_path_skips_rather_than_guessing_a_directory()
    {
        var environment = Environment(qrf: null);

        var plan = CompilePlan.Create(Ticket(Qrf("rep.p")), environment, "pilot");

        Assert.Contains("qrfRemotePath", Assert.Single(plan.Skipped).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_custom_statement_template_is_used_verbatim()
    {
        var environment = Environment(
            recipe: new EditorCompileSettings("editor", null, [], EditorCompileSettings.DefaultSteps, false, "COMPILE {remoteFile} SAVE INTO {remoteDirectory} XREF x."));

        var compile = Assert.Single(
            CompilePlan.Create(Ticket(Qrf("rep.p")), environment, "pilot").Using<PlannedEditorCompile>());

        Assert.Equal($"COMPILE {QrfPath}/rep.p SAVE INTO {QrfPath} XREF x.", compile.Statement);
    }

    [Fact]
    public void An_empty_ticket_plans_nothing_and_skips_nothing()
    {
        var plan = CompilePlan.Create(new TicketFolder("T", "p", []), Environment(), "pilot");

        Assert.True(plan.IsEmpty);
        Assert.Empty(plan.Skipped);
    }

    // --- helpers ---------------------------------------------------------

    private static ProgramFile Qrf(string name) =>
        new(ProgramKind.Qrf, Path.Combine(@"C:\tasks\T", "QRF", name));

    private static ProgramFile Src(string name) =>
        new(ProgramKind.Src, Path.Combine(@"C:\tasks\T", "SRC", name));

    private static TicketFolder Ticket(params ProgramFile[] files) =>
        new("Ticket 9999555", @"C:\tasks\T", files);

    private static CompilePlan Plan(params ProgramFile[] files) =>
        CompilePlan.Create(Ticket(files), Environment(), "pilot");

    private static readonly EditorCompileSettings DefaultRecipe =
        new(
            "compile_editor us test",
            null,
            [],
            EditorCompileSettings.DefaultSteps,
            false,
            EditorCompileSettings.DefaultStatementTemplate);

    /// <summary>An environment that can compile QRF, unless told otherwise.</summary>
    private static QadEnvironment Environment(
        string? qrf = QrfPath,
        EditorCompileSettings? recipe = null) =>
        Build(qrf, recipe ?? DefaultRecipe);

    /// <summary>An environment whose QRF recipe has not been worked out yet.</summary>
    private static QadEnvironment EnvironmentWithoutRecipe() => Build(QrfPath, null);

    private static QadEnvironment Build(string? qrf, EditorCompileSettings? recipe) =>
        new(
            "TEST",
            false,
            new SshEndpoint("qad.example", 22, "mfg", "hunter2", null),
            new RemotePaths(SrcPath, qrf),
            new CompileSettings(recipe is null ? null : new QrfCompileSettings(recipe, null), null));
}
