using QadAutomation.Core.Configuration.Raw;

namespace QadAutomation.Core.Configuration;

/// <summary>
/// Turns the permissive <see cref="ConfigurationFile"/> into a validated
/// <see cref="ToolConfiguration"/>, applying client defaults to each environment.
/// </summary>
/// <remarks>
/// <para>
/// This class is deliberately pure: it touches no filesystem, no clock, no
/// environment variables. Everything it needs arrives as an argument. That makes
/// the whole of the merge-and-validate policy - easily the fiddliest logic in the
/// configuration story - testable with plain object literals and no test doubles
/// at all.
/// </para>
/// <para>
/// It also collects <i>every</i> problem before throwing. Fixing a config file one
/// error per run is a miserable way to spend an afternoon.
/// </para>
/// </remarks>
public sealed class ConfigurationResolver
{
    private const int DefaultSshPort = 22;

    /// <summary>
    /// Environment names that are treated as production unless the config says
    /// otherwise. Guessing wrong in this direction only costs an extra
    /// confirmation prompt; guessing wrong the other way costs a bad PROD deploy.
    /// </summary>
    private static readonly HashSet<string> ProductionNames =
        new(StringComparer.OrdinalIgnoreCase) { "PROD", "PRD", "LIVE", "PRODUCTION" };

    /// <param name="file">The deserialised configuration file.</param>
    /// <param name="sourcePath">Only used to make error messages point somewhere.</param>
    /// <exception cref="ConfigurationException">If anything is missing or invalid.</exception>
    public ToolConfiguration Resolve(ConfigurationFile file, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(file);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(file.WorkingFolder))
        {
            errors.Add("'workingFolder' is required (the local 'QAD Tasks' folder).");
        }

        var clients = new List<ClientProfile>();
        var seenClientIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (file.Clients is null || file.Clients.Count == 0)
        {
            errors.Add("'clients' must contain at least one client.");
        }
        else
        {
            for (var i = 0; i < file.Clients.Count; i++)
            {
                var resolved = ResolveClient(file.Clients[i], i, seenClientIds, errors);
                if (resolved is not null)
                {
                    clients.Add(resolved);
                }
            }
        }

        if (errors.Count > 0)
        {
            throw ConfigurationException.FromErrors(sourcePath, errors);
        }

        return new ToolConfiguration(file.WorkingFolder!.Trim(), clients);
    }

    private static ClientProfile? ResolveClient(
        ClientSection client,
        int index,
        ISet<string> seenIds,
        List<string> errors)
    {
        // Without an id there is nothing to label later errors with, so this one
        // problem stops us resolving the rest of the client.
        if (string.IsNullOrWhiteSpace(client.Id))
        {
            errors.Add($"clients[{index}]: 'id' is required.");
            return null;
        }

        var id = client.Id.Trim();
        if (!seenIds.Add(id))
        {
            errors.Add($"Client id '{id}' is used more than once; ids must be unique.");
            return null;
        }

        var vpn = ResolveVpn(client.Vpn, id, errors);

        var environments = new List<QadEnvironment>();
        var seenEnvironmentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (client.Environments is null || client.Environments.Count == 0)
        {
            errors.Add($"Client '{id}': 'environments' must contain at least one environment.");
        }
        else
        {
            foreach (var section in client.Environments)
            {
                var resolved = ResolveEnvironment(section, client.Defaults, id, seenEnvironmentNames, errors);
                if (resolved is not null)
                {
                    environments.Add(resolved);
                }
            }
        }

        // DisplayName is a convenience, not a requirement: fall back to the id
        // rather than making the operator repeat themselves.
        var displayName = string.IsNullOrWhiteSpace(client.DisplayName)
            ? id
            : client.DisplayName.Trim();

        return new ClientProfile(id, displayName, vpn, environments);
    }

    private static VpnSettings ResolveVpn(VpnSection? vpn, string clientId, List<string> errors)
    {
        // An absent vpn block means "the tool does not manage the VPN". That is a
        // legitimate, and for FortiClient clients currently the preferred, setup.
        if (vpn is null || string.IsNullOrWhiteSpace(vpn.Type))
        {
            return new VpnSettings(VpnType.None, null, null, null);
        }

        if (!Enum.TryParse<VpnType>(vpn.Type.Trim(), ignoreCase: true, out var type))
        {
            errors.Add(
                $"Client '{clientId}': unknown vpn type '{vpn.Type}'. " +
                $"Expected one of: {string.Join(", ", Enum.GetNames<VpnType>())}.");
            return new VpnSettings(VpnType.None, null, null, null);
        }

        // rasdial addresses a VPN by the name of a saved Windows connection, so
        // without that name a WindowsRas profile cannot do anything at all.
        if (type == VpnType.WindowsRas && string.IsNullOrWhiteSpace(vpn.ConnectionName))
        {
            errors.Add(
                $"Client '{clientId}': vpn type 'WindowsRas' requires 'connectionName' " +
                $"(the name of the saved Windows VPN connection).");
        }

        return new VpnSettings(
            type,
            Trimmed(vpn.ConnectionName),
            Trimmed(vpn.Username),
            vpn.Password);
    }

    private static QadEnvironment? ResolveEnvironment(
        EnvironmentSection environment,
        EnvironmentSection? defaults,
        string clientId,
        ISet<string> seenNames,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(environment.Name))
        {
            errors.Add($"Client '{clientId}': every environment needs a 'name' (e.g. DEVL, TEST, PROD).");
            return null;
        }

        var name = environment.Name.Trim();
        if (!seenNames.Add(name))
        {
            errors.Add($"Client '{clientId}': environment '{name}' is defined more than once.");
            return null;
        }

        var label = $"Client '{clientId}', environment '{name}'";

        // The override rule, in one place: a value set on the environment wins,
        // otherwise the client default applies. Null means "not specified", which
        // is exactly why the raw layer keeps everything nullable.
        var host = Trimmed(environment.Host) ?? Trimmed(defaults?.Host);
        var port = environment.Port ?? defaults?.Port ?? DefaultSshPort;
        var username = Trimmed(environment.Username) ?? Trimmed(defaults?.Username);
        var password = environment.Password ?? defaults?.Password;
        var privateKeyPath = Trimmed(environment.PrivateKeyPath) ?? Trimmed(defaults?.PrivateKeyPath);

        if (host is null)
        {
            errors.Add($"{label}: 'host' is required (set it on the environment or in the client's 'defaults').");
        }

        if (username is null)
        {
            errors.Add($"{label}: 'username' is required.");
        }

        if (port is < 1 or > 65535)
        {
            errors.Add($"{label}: 'port' must be between 1 and 65535 but was {port}.");
        }

        if (string.IsNullOrWhiteSpace(password) && privateKeyPath is null)
        {
            errors.Add($"{label}: needs either a 'password' or a 'privateKeyPath' to authenticate.");
        }

        var paths = new RemotePaths(
            Trimmed(environment.SrcRemotePath) ?? Trimmed(defaults?.SrcRemotePath),
            Trimmed(environment.QrfRemotePath) ?? Trimmed(defaults?.QrfRemotePath));

        var compile = ResolveCompile(environment.Compile ?? defaults?.Compile, label, errors);

        // If the environment failed validation we still return null so that the
        // caller does not build a half-valid object; the collected errors are what
        // the operator will see.
        if (host is null || username is null || compile is null)
        {
            return null;
        }

        var isProduction = environment.IsProduction ?? ProductionNames.Contains(name);

        return new QadEnvironment(
            name,
            isProduction,
            new SshEndpoint(host, port, username, NullIfBlank(password), privateKeyPath),
            paths,
            compile);
    }

    private static CompileSettings? ResolveCompile(
        CompileSection? compile,
        string label,
        List<string> errors)
    {
        // Note: 'compile' is replaced wholesale by an environment, never merged
        // field-by-field. A half-inherited compile recipe - this environment's
        // strategy with another's commands - is far more likely to be a surprise
        // than a convenience.
        if (compile is null || string.IsNullOrWhiteSpace(compile.Strategy))
        {
            errors.Add($"{label}: a 'compile' block with a 'strategy' is required.");
            return null;
        }

        if (!Enum.TryParse<CompileStrategy>(compile.Strategy.Trim(), ignoreCase: true, out var strategy))
        {
            errors.Add(
                $"{label}: unknown compile strategy '{compile.Strategy}'. " +
                $"Expected one of: {string.Join(", ", Enum.GetNames<CompileStrategy>())}.");
            return null;
        }

        var commands = compile.Commands?
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .ToList() ?? [];

        // An InteractiveMenu recipe is allowed to be empty for now - those flows
        // are still being mapped out - but a DirectCommand with nothing to run is
        // simply an incomplete config.
        if (strategy == CompileStrategy.DirectCommand && commands.Count == 0)
        {
            errors.Add($"{label}: compile strategy 'DirectCommand' requires at least one entry in 'commands'.");
            return null;
        }

        return new CompileSettings(strategy, commands);
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
