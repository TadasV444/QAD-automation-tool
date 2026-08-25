namespace QadAutomation.Core.Configuration.Raw;

/*
 * The raw layer is a faithful, permissive mirror of the JSON file: every member
 * is nullable, nothing is validated, nothing has behaviour.
 *
 * Why a separate layer instead of deserialising straight into the domain records?
 *
 *  1. "Absent" and "empty" are different things here. An environment that omits
 *     `host` should inherit the client default; one that sets it to "" is a
 *     mistake. Only a nullable raw shape can tell those apart.
 *  2. The domain records can then be non-nullable and always-valid. Code further
 *     down the pipeline never re-checks whether a host is present.
 *  3. The file format can change independently of the domain model - a rename in
 *     the JSON touches one class here, not the whole codebase.
 *
 * These are mutable classes with settable properties because that is what
 * System.Text.Json needs; they are internal to the configuration namespace's
 * contract and never escape into the rest of the application.
 */

/// <summary>Root object of <c>config.json</c>.</summary>
public sealed class ConfigurationFile
{
    public string? WorkingFolder { get; set; }
    public List<ClientSection>? Clients { get; set; }
}

/// <summary>One entry in the <c>clients</c> array.</summary>
public sealed class ClientSection
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
    public VpnSection? Vpn { get; set; }

    /// <summary>
    /// Values inherited by every environment of this client. Cuts the repetition
    /// out of a 4-clients x 3-environments file, where most fields are identical.
    /// </summary>
    public EnvironmentSection? Defaults { get; set; }

    public List<EnvironmentSection>? Environments { get; set; }
}

/// <summary>
/// Used both for a client's <c>defaults</c> and for each environment. Sharing one
/// shape is what makes the override rule trivial: same field, environment wins.
/// </summary>
public sealed class EnvironmentSection
{
    public string? Name { get; set; }
    public bool? IsProduction { get; set; }

    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? PrivateKeyPath { get; set; }

    public string? SrcRemotePath { get; set; }
    public string? QrfRemotePath { get; set; }

    public CompileSection? Compile { get; set; }
}

/// <summary>VPN parameters for a client.</summary>
public sealed class VpnSection
{
    /// <summary>Parsed into <see cref="VpnType"/>; case-insensitive.</summary>
    public string? Type { get; set; }

    public string? ConnectionName { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>
    /// Distinctive text from the tunnel's network adapter. Only for types the
    /// tool verifies rather than dials, and only when the defaults miss it.
    /// </summary>
    public string? AdapterName { get; set; }
}

/// <summary>Compile recipes for a client default or a single environment.</summary>
public sealed class CompileSection
{
    public QrfCompileSection? Qrf { get; set; }
    public SrcCompileSection? Src { get; set; }
}

/// <summary>The <c>compile.qrf</c> block. Exactly one procedure is expected.</summary>
public sealed class QrfCompileSection
{
    public EditorCompileSection? Editor { get; set; }
    public ShellCompileSection? Shell { get; set; }
}

/// <summary>The <c>compile.src</c> block. Exactly one procedure is expected.</summary>
public sealed class SrcCompileSection
{
    public ManifestCompileSection? Manifest { get; set; }
    public ShellCompileSection? Shell { get; set; }
}

/// <summary>Driving an interactive editor wrapper.</summary>
public sealed class EditorCompileSection
{
    /// <summary>With <c>{language}</c> substituted where the wrapper takes one.</summary>
    public string? EditorCommand { get; set; }

    /// <summary>Optional; omit where the command is an absolute path.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Optional; omit where the wrapper is language-neutral.</summary>
    public List<string>? Languages { get; set; }

    /// <summary>
    /// Optional. Named steps - Statement, Enter, Go, NewBuffer - in the order
    /// they are sent per program. Defaults to NewBuffer, Statement, Go.
    /// </summary>
    public List<string>? Steps { get; set; }

    /// <summary>Optional; true where the editor cannot take a second program.</summary>
    public bool? RestartPerFile { get; set; }

    /// <summary>Optional; defaults to the standard Progress compile statement.</summary>
    public string? Statement { get; set; }
}

/// <summary>A manifest file plus one script run per language.</summary>
public sealed class ManifestCompileSection
{
    public string? WorkingDirectory { get; set; }

    /// <summary>Run once per language, with <c>{language}</c> substituted.</summary>
    public string? Command { get; set; }

    /// <summary>Language code to where that language reads and writes.</summary>
    public Dictionary<string, LanguageTargetSection>? Languages { get; set; }
}

/// <summary>One entry in <c>compile.src.manifest.languages</c>.</summary>
public sealed class LanguageTargetSection
{
    public string? ManifestPath { get; set; }

    /// <summary>Root the compiled output lands under, above the prefix folder.</summary>
    public string? ResultPath { get; set; }
}

/// <summary>One ordinary shell command.</summary>
public sealed class ShellCompileSection
{
    public string? WorkingDirectory { get; set; }
    public string? Command { get; set; }

    /// <summary>
    /// Optional. Where the command writes compiled output, with <c>{prefix}</c>
    /// and <c>{name}</c> substituted. Without it the exit code alone decides.
    /// </summary>
    public string? ResultPath { get; set; }
}
