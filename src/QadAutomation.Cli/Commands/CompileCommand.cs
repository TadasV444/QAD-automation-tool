using System.Text.RegularExpressions;
using QadAutomation.Core.Compile;
using QadAutomation.Core.Configuration;
using QadAutomation.Core.Tickets;
using QadAutomation.Core.Vpn;

namespace QadAutomation.Cli.Commands;

/// <summary>
/// <c>qad compile &lt;client&gt; &lt;environment&gt; &lt;ticket&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <see cref="UploadCommand"/> - plan, print, confirm,
/// connect, act - because the two will eventually be halves of one
/// <c>qad deploy</c>, and a step that behaved differently would be the one an
/// operator got wrong.
/// </para>
/// <para>
/// It exits non-zero when anything was skipped, not only when a compile failed.
/// A ticket whose SRC half was never built, reported as success, is precisely
/// the mistake this tool exists to make impossible.
/// </para>
/// </remarks>
public sealed class CompileCommand
{
    private readonly IConfigurationLoader _loader;
    private readonly ITicketFolderReader _tickets;
    private readonly IVpnConnectorFactory _connectors;
    private readonly IProgramCompiler _compiler;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public CompileCommand(
        IConfigurationLoader loader,
        ITicketFolderReader tickets,
        IVpnConnectorFactory connectors,
        IProgramCompiler compiler,
        TextWriter output,
        TextWriter error)
    {
        _loader = loader;
        _tickets = tickets;
        _connectors = connectors;
        _compiler = compiler;
        _output = output;
        _error = error;
    }

    public int Execute(string clientId, string environmentName, string ticketName, CompileOptions options)
    {
        var configuration = _loader.Load().Configuration;
        var client = configuration.RequireClient(clientId);
        var environment = client.RequireEnvironment(environmentName);

        var ticket = _tickets.Read(ticketName);
        var plan = CompilePlan.Create(ticket, environment, client.Id);

        WritePlan(client, plan);

        if (plan.IsEmpty)
        {
            // Skips have already been printed. Nothing to compile is only "Ok"
            // when there was also nothing to skip.
            _output.WriteLine("Nothing to compile.");
            return plan.Skipped.Count == 0 ? ExitCode.Ok : ExitCode.UsageError;
        }

        if (options.DryRun)
        {
            _output.WriteLine("Dry run - nothing was compiled.");
            return ExitCode.Ok;
        }

        if (plan.IsProduction && !options.Confirmed)
        {
            _error.WriteLine(
                $"'{environment.Name}' is a PRODUCTION environment. Re-run with --yes to go ahead, " +
                "or with --dry-run to see the plan again.");
            return ExitCode.UsageError;
        }

        var connector = _connectors.Create(client.Vpn);

        using var session = connector.Connect(client.Vpn);

        if (session.OpenedByTool)
        {
            _output.WriteLine($"VPN '{session.ConnectionName}' connected.");
        }

        var outcome = _compiler.Compile(plan, environment.Ssh, line => _output.WriteLine(line));

        return WriteOutcome(_output, _error, outcome);
    }

    private void WritePlan(ClientProfile client, CompilePlan plan)
    {
        PlanDisplay.WriteHeader(_output, client, plan.TicketName, plan.Environment);
        PlanDisplay.WriteCompilePlan(_output, plan);
    }

    internal static int WriteOutcome(TextWriter output, TextWriter error, CompileOutcome outcome)
    {
        output.WriteLine();
        output.WriteLine($"Done - {outcome.CompiledCount} compiled, {outcome.FailedCount} failed.");

        foreach (var failure in outcome.Failures)
        {
            error.WriteLine();
            error.WriteLine($"{failure.Planned.File.FileName} did not compile.");

            // Every expected result, not just the first. A SRC program builds
            // once per language, and "lt moved but us did not" is the state
            // worth seeing spelled out.
            foreach (var result in failure.Planned.RemoteResults)
            {
                error.WriteLine($"  {result} was not updated.");
            }

            var screen = Readable(failure.Screen);

            if (screen.Count > 0)
            {
                error.WriteLine("  The server showed:");

                foreach (var line in screen)
                {
                    error.WriteLine($"    {line}");
                }
            }
        }

        if (outcome.FailedCount > 0)
        {
            return ExitCode.CompileError;
        }

        // Skips were listed before the run; this is the reminder at the end,
        // where the operator is actually deciding whether they are finished.
        return outcome.Skipped.Count == 0 ? ExitCode.Ok : ExitCode.UsageError;
    }

    /// <summary>
    /// Turns a captured terminal screen into something worth printing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The editor positions its cursor rather than emitting newlines, so the raw
    /// capture arrives as one long string interleaved with escape sequences. Each
    /// sequence becomes a line break here - that is what recovers the structure,
    /// since a jump to a new row is the only thing standing in for a newline.
    /// </para>
    /// <para>
    /// It also draws a box around errors using the DEC line-drawing character
    /// set, which arrives as runs of <c>q</c> with <c>x</c> down the sides. Those
    /// rows carry nothing, so they go.
    /// </para>
    /// <para>
    /// All of this is presentation. Nothing here has any say in whether the
    /// compile succeeded - that was settled by the <c>.r</c> timestamp before
    /// this method is ever called - so an imperfect filter costs readability and
    /// never correctness.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> Readable(string screen)
    {
        if (string.IsNullOrWhiteSpace(screen))
        {
            return [];
        }

        return [.. EscapeSequence.Split(screen)
            .Select(Clean)
            // Three characters, because splitting on escape sequences chops the
            // box's "<OK>" button into fragments like "<" and "K>". Progress
            // messages are whole sentences, so nothing real is this short.
            .Where(line => line.Length >= 3)
            .Distinct(StringComparer.Ordinal)
            .Take(20)];
    }

    /// <summary>
    /// ANSI escape sequences: cursor moves, colour, and character-set selection.
    /// </summary>
    /// <remarks>
    /// Non-capturing: <see cref="Regex.Split(string)"/> returns the contents of
    /// capturing groups as elements, which would put the escape sequences
    /// straight back into the output it is meant to remove them from.
    /// </remarks>
    private static readonly Regex EscapeSequence =
        new(@"\x1b(?:\[[0-9;?]*[ -/]*[@-~]|[()][0-9A-Za-z]|.)", RegexOptions.Compiled);

    /// <summary>
    /// Removes the box the editor draws around an error, leaving its contents.
    /// </summary>
    /// <remarks>
    /// Progress draws with the DEC line-drawing set, which arrives as ASCII:
    /// runs of <c>q</c> for horizontal rules, <c>x</c> for the sides, and
    /// <c>l m k j</c> for the corners. A rule row collapses to nothing and is
    /// dropped by the length filter; a titled row like
    /// <c>lqqq Error qqqk</c> collapses to <c>Error</c>, which is worth keeping.
    /// </remarks>
    private static string Clean(string fragment)
    {
        var text = HorizontalRule.Replace(
            new string([.. fragment.Where(c => !char.IsControl(c))]), " ").Trim();

        if (text.Length > 0 && text[0] is 'l' or 'm' or 'x' or 't')
        {
            text = text[1..];
        }

        if (text.Length > 0 && text[^1] is 'k' or 'j' or 'x' or 'u')
        {
            text = text[..^1];
        }

        return text.Trim();
    }

    /// <summary>
    /// Three or more, so a real word ending in <c>qq</c> is never touched.
    /// </summary>
    private static readonly Regex HorizontalRule = new("q{3,}", RegexOptions.Compiled);
}

/// <summary>Switches affecting how a compile runs.</summary>
/// <param name="DryRun">Print the plan and stop.</param>
/// <param name="Confirmed">The operator passed <c>--yes</c>.</param>
public sealed record CompileOptions(bool DryRun, bool Confirmed);
