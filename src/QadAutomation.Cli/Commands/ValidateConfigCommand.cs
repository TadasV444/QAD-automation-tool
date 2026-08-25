using QadAutomation.Cli.Output;
using QadAutomation.Core.Configuration;

namespace QadAutomation.Cli.Commands;

/// <summary>
/// <c>qad validate</c> - loads the configuration and prints a redacted summary.
/// </summary>
/// <remarks>
/// This is the first thing an engineer runs after setting the tool up, and the
/// first thing to run after editing the config. It answers "which file am I
/// using, did it parse, and did it resolve into the targets I expected?" while
/// making no network calls whatsoever - the whole of step 1 is safe to run
/// against a production config without touching production.
/// </remarks>
public sealed class ValidateConfigCommand
{
    private readonly IConfigurationLoader _loader;
    private readonly TextWriter _output;

    public ValidateConfigCommand(IConfigurationLoader loader, TextWriter output)
    {
        _loader = loader;
        _output = output;
    }

    public int Execute()
    {
        var loaded = _loader.Load();
        var configuration = loaded.Configuration;

        _output.WriteLine($"Configuration : {loaded.SourcePath}");
        _output.WriteLine($"Working folder: {configuration.WorkingFolder}");

        // The config can be perfectly valid while pointing at a folder that does
        // not exist yet, so this is a warning rather than a validation error.
        if (!Directory.Exists(configuration.WorkingFolder))
        {
            _output.WriteLine("                ^ warning: this folder does not exist yet");
        }

        _output.WriteLine();

        foreach (var client in configuration.Clients)
        {
            WriteClient(client);
        }

        var environmentCount = configuration.Clients.Sum(c => c.Environments.Count);
        _output.WriteLine(
            $"OK - {configuration.Clients.Count} client(s), {environmentCount} environment(s).");

        return ExitCode.Ok;
    }

    private void WriteClient(ClientProfile client)
    {
        _output.WriteLine($"{client.DisplayName}  [{client.Id}]");
        _output.WriteLine($"  VPN         : {DescribeVpn(client.Vpn)}");

        foreach (var environment in client.Environments)
        {
            // PROD is called out visually because everything dangerous this tool
            // will eventually do is dangerous specifically there.
            var marker = environment.IsProduction ? "  ** PRODUCTION **" : string.Empty;

            // Aliases shown here and nowhere else in the output: this is the
            // screen an operator reads to learn what they may type. Everything
            // afterwards reports by the canonical name only.
            _output.WriteLine($"  {environment.Described}{marker}");

            var ssh = environment.Ssh;
            var auth = ssh.UsesKeyAuthentication
                ? $"key: {ssh.PrivateKeyPath}"
                : $"password: {SecretDisplay.Describe(ssh.Password)}";

            _output.WriteLine($"    ssh     : {ssh.Username}@{ssh.Host}:{ssh.Port}  ({auth})");
            _output.WriteLine($"    SRC path: {environment.Paths.Src ?? "(none)"}");
            _output.WriteLine($"    QRF path: {environment.Paths.Qrf ?? "(none)"}");
            _output.WriteLine($"    compile : {DescribeCompile(environment.Compile)}");
        }

        _output.WriteLine();
    }

    /// <summary>
    /// Names which kinds this environment can compile.
    /// </summary>
    /// <remarks>
    /// Says "QRF only" rather than listing the editor command. What the operator
    /// needs from this screen is whether a kind will work at all; the command
    /// itself is in the file they are already looking at.
    /// </remarks>
    private static string DescribeCompile(CompileSettings compile) => compile switch
    {
        { Qrf: not null, Src: not null } => "SRC and QRF",
        { Qrf: not null } => "QRF only (no 'compile.src' block)",
        { Src: not null } => "SRC only (no 'compile.qrf' block)",
        _ => "not configured"
    };

    private static string DescribeVpn(VpnSettings vpn) => vpn.Type switch
    {
        VpnType.None => "not managed by this tool (connect manually)",
        VpnType.WindowsRas =>
            $"Windows RAS, connection '{vpn.ConnectionName}', " +
            $"password {SecretDisplay.Describe(vpn.Password)}",

        // Says "checked", not "connected". This screen is where an operator
        // forms their idea of what the tool will do for them, and believing it
        // dials a tunnel it only inspects is the misunderstanding to prevent.
        VpnType.FortiClient =>
            $"FortiClient tunnel '{vpn.ConnectionName}' - checked, not connected " +
            $"(open it in FortiClient yourself); adapter matched on " +
            $"{(vpn.AdapterName is { } name ? $"'{name}'" : "the vendor's usual names")}",

        _ => vpn.Type.ToString()
    };
}
