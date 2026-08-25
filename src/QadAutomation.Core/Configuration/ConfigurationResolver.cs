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

        // FortiClient is verified, not dialled, and the message that says so
        // names the tunnel. Without a name the operator is told to connect
        // something the tool cannot identify.
        if (type == VpnType.FortiClient && string.IsNullOrWhiteSpace(vpn.ConnectionName))
        {
            errors.Add(
                $"Client '{clientId}': vpn type 'FortiClient' requires 'connectionName' " +
                "(the tunnel's name in FortiClient), so the tool can name it when asking " +
                "you to connect it.");
        }

        return new VpnSettings(
            type,
            Trimmed(vpn.ConnectionName),
            Trimmed(vpn.Username),
            vpn.Password,
            Trimmed(vpn.AdapterName));
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

        var aliases = new List<string>();

        // Aliases share the name space, because they are matched the same way.
        // An alias colliding with another environment's name would make
        // 'qad deploy <client> prod' ambiguous - which is the single worst
        // thing for this tool to be ambiguous about.
        foreach (var alias in Trimmed(environment.Aliases))
        {
            if (seenNames.Add(alias))
            {
                aliases.Add(alias);
                continue;
            }

            errors.Add(
                $"{label}: alias '{alias}' is already used by this client, as a name or " +
                "another alias. Every environment must be unambiguous.");
            return null;
        }

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

        // Inferred from the canonical name only. An alias like 'euro' is exactly
        // the case where a site's own word does not look dangerous, so letting
        // it decide would defeat the guard it is meant to trigger. Where the
        // name does not give it away, 'isProduction' says so explicitly.
        var isProduction = environment.IsProduction ?? ProductionNames.Contains(name);

        return new QadEnvironment(
            name,
            isProduction,
            new SshEndpoint(host, port, username, NullIfBlank(password), privateKeyPath),
            paths,
            compile,
            aliases);
    }

    /// <summary>
    /// Builds the compile recipes, or returns <c>null</c> if one is present but
    /// malformed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Note: 'compile' is replaced wholesale by an environment, never merged
    /// field-by-field. A half-inherited recipe - this environment's editor
    /// command with another's manifest path - is far more likely to be a
    /// surprise than a convenience.
    /// </para>
    /// <para>
    /// A missing block is not an error. Uploading works without compiling, and
    /// SRC compilation has not been verified anywhere yet; refusing to load a
    /// config until every recipe is filled in would push someone to invent one.
    /// The failure surfaces at the point of use instead, where it can say which
    /// kind is missing for which environment.
    /// </para>
    /// </remarks>
    private static CompileSettings? ResolveCompile(
        CompileSection? compile,
        string label,
        List<string> errors)
    {
        if (compile is null)
        {
            return new CompileSettings(null, null);
        }

        var before = errors.Count;

        var qrf = ResolveQrfCompile(compile.Qrf, label, errors);
        var src = ResolveSrcCompile(compile.Src, label, errors);

        return errors.Count == before ? new CompileSettings(qrf, src) : null;
    }

    private static QrfCompileSettings? ResolveQrfCompile(
        QrfCompileSection? qrf,
        string label,
        List<string> errors)
    {
        if (qrf is null)
        {
            return null;
        }

        if (!ExactlyOne(qrf.Editor, qrf.Shell, "compile.qrf", "editor, shell", label, errors))
        {
            return null;
        }

        var editor = ResolveEditorCompile(qrf.Editor, label, errors);
        var shell = ResolveShellCompile(qrf.Shell, "compile.qrf.shell", label, errors);

        return editor is null && shell is null ? null : new QrfCompileSettings(editor, shell);
    }

    private static SrcCompileSettings? ResolveSrcCompile(
        SrcCompileSection? src,
        string label,
        List<string> errors)
    {
        if (src is null)
        {
            return null;
        }

        if (!ExactlyOne(src.Manifest, src.Shell, "compile.src", "manifest, shell", label, errors))
        {
            return null;
        }

        var manifest = ResolveManifestCompile(src.Manifest, label, errors);
        var shell = ResolveShellCompile(src.Shell, "compile.src.shell", label, errors);

        return manifest is null && shell is null ? null : new SrcCompileSettings(manifest, shell);
    }

    /// <summary>
    /// Checks a kind names one procedure and not two.
    /// </summary>
    /// <remarks>
    /// Both would be ambiguous and neither is an empty block that reads as
    /// configured. Either way the operator believes something will be compiled
    /// that will not be.
    /// </remarks>
    private static bool ExactlyOne(
        object? first, object? second, string block, string options, string label, List<string> errors)
    {
        var count = (first is null ? 0 : 1) + (second is null ? 0 : 1);

        if (count == 1)
        {
            return true;
        }

        errors.Add(count == 0
            ? $"{label}: '{block}' must name a procedure - one of: {options}."
            : $"{label}: '{block}' names more than one procedure. Keep one of: {options}.");

        return false;
    }

    private static EditorCompileSettings? ResolveEditorCompile(
        EditorCompileSection? editor,
        string label,
        List<string> errors)
    {
        if (editor is null)
        {
            return null;
        }

        const string block = "compile.qrf.editor";

        var editorCommand = Trimmed(editor.EditorCommand);

        if (editorCommand is null)
        {
            errors.Add($"{label}: '{block}' needs an 'editorCommand'.");
            return null;
        }

        var statement = Trimmed(editor.Statement) ?? EditorCompileSettings.DefaultStatementTemplate;

        if (!statement.Contains("{remoteFile}", StringComparison.Ordinal))
        {
            errors.Add(
                $"{label}: '{block}.statement' must contain '{{remoteFile}}', " +
                "otherwise every report would compile the same file.");
            return null;
        }

        // Only when it is a statement rather than a bare path. One site's
        // wrapper wants a Progress COMPILE line, and one wants the filename on
        // its own; the space is what tells them apart.
        //
        // The rule earns its place on the first: a Progress statement missing
        // its full stop is not an error anyone sees. The editor holds an
        // unterminated line, the run key compiles nothing, and it looks exactly
        // like a compile that ran and changed nothing.
        if (statement.Any(char.IsWhiteSpace) && !statement.EndsWith('.'))
        {
            errors.Add(
                $"{label}: '{block}.statement' looks like a Progress statement and must " +
                "end with '.'. If it is meant to be a bare path, remove the spaces.");
            return null;
        }

        var languages = Trimmed(editor.Languages);

        if (editorCommand.Contains("{language}", StringComparison.Ordinal) && languages.Count == 0)
        {
            errors.Add(
                $"{label}: '{block}.editorCommand' contains '{{language}}' but no " +
                "'languages' are listed, so it could never be substituted.");
            return null;
        }

        var steps = ResolveSteps(editor.Steps, block, label, errors);

        if (steps is null)
        {
            return null;
        }

        if (!steps.Contains(EditorStep.Statement))
        {
            errors.Add($"{label}: '{block}.steps' must include 'Statement' - otherwise nothing is typed.");
            return null;
        }

        return new EditorCompileSettings(
            editorCommand,
            Trimmed(editor.WorkingDirectory),
            languages,
            steps,
            editor.RestartPerFile ?? false,
            statement);
    }

    /// <summary>
    /// Parses the named keystroke steps, or <c>null</c> if any is unknown.
    /// </summary>
    /// <remarks>
    /// An unrecognised step cannot be skipped: the sequence is the procedure,
    /// and running it with a gap would send the remaining keys to a window
    /// expecting something else.
    /// </remarks>
    private static IReadOnlyList<EditorStep>? ResolveSteps(
        List<string>? steps, string block, string label, List<string> errors)
    {
        if (steps is null || steps.Count == 0)
        {
            return EditorCompileSettings.DefaultSteps;
        }

        var parsed = new List<EditorStep>(steps.Count);

        foreach (var step in steps.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            if (!Enum.TryParse<EditorStep>(step.Trim(), ignoreCase: true, out var value))
            {
                errors.Add(
                    $"{label}: '{block}.steps' has an unknown step '{step}'. " +
                    $"Expected one of: {string.Join(", ", Enum.GetNames<EditorStep>())}.");
                return null;
            }

            parsed.Add(value);
        }

        return parsed;
    }

    private static IReadOnlyList<string> Trimmed(List<string>? values) =>
        [.. (values ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim())];

    private static ManifestCompileSettings? ResolveManifestCompile(
        ManifestCompileSection? manifest,
        string label,
        List<string> errors)
    {
        if (manifest is null)
        {
            return null;
        }

        const string block = "compile.src.manifest";

        var workingDirectory = Trimmed(manifest.WorkingDirectory);
        var command = Trimmed(manifest.Command);
        var languages = ResolveLanguageTargets(manifest.Languages, block, label, errors);

        if (workingDirectory is null)
        {
            errors.Add($"{label}: '{block}' needs a 'workingDirectory' for the compile script.");
        }

        if (command is null)
        {
            errors.Add($"{label}: '{block}' needs a 'command', e.g. './compile {{language}} test'.");
        }
        else if (!command.Contains("{language}", StringComparison.Ordinal))
        {
            // Without the placeholder the same language would be compiled once
            // per entry, and the other language's output would never move -
            // which the verification would correctly, but confusingly, call a
            // failure.
            errors.Add($"{label}: '{block}.command' must contain '{{language}}'.");
        }

        return workingDirectory is null || command is null || languages is null
            ? null
            : new ManifestCompileSettings(workingDirectory, command, languages);
    }

    private static IReadOnlyDictionary<string, LanguageTarget>? ResolveLanguageTargets(
        Dictionary<string, LanguageTargetSection>? languages, string block, string label, List<string> errors)
    {
        var resolved = new Dictionary<string, LanguageTarget>(StringComparer.Ordinal);

        foreach (var (code, target) in languages ?? [])
        {
            if (string.IsNullOrWhiteSpace(code) || target is null)
            {
                continue;
            }

            var manifestPath = Trimmed(target.ManifestPath);
            var resultPath = Trimmed(target.ResultPath);

            if (manifestPath is null || resultPath is null)
            {
                errors.Add(
                    $"{label}: '{block}.languages.{code}' needs both a 'manifestPath' - the list " +
                    "of programs that language compiles - and a 'resultPath', where its output lands.");
                return null;
            }

            resolved[code.Trim()] = new LanguageTarget(manifestPath, resultPath);
        }

        if (resolved.Count > 0)
        {
            return resolved;
        }

        errors.Add(
            $"{label}: '{block}' needs at least one entry in 'languages', mapping a language " +
            "code to its manifest and output directories.");

        return null;
    }

    private static ShellCompileSettings? ResolveShellCompile(
        ShellCompileSection? shell,
        string block,
        string label,
        List<string> errors)
    {
        if (shell is null)
        {
            return null;
        }

        var workingDirectory = Trimmed(shell.WorkingDirectory);
        var command = Trimmed(shell.Command);

        if (workingDirectory is null)
        {
            errors.Add($"{label}: '{block}' needs a 'workingDirectory'.");
        }

        if (command is null)
        {
            errors.Add($"{label}: '{block}' needs a 'command'.");
        }

        var resultPath = Trimmed(shell.ResultPath);

        if (resultPath is not null && !resultPath.Contains("{name}", StringComparison.Ordinal))
        {
            // Without it every program would be checked against one path, so a
            // single stale file would report the whole ticket as failed.
            errors.Add($"{label}: '{block}.resultPath' must contain '{{name}}'.");
            return null;
        }

        return workingDirectory is null || command is null
            ? null
            : new ShellCompileSettings(workingDirectory, command, resultPath);
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
