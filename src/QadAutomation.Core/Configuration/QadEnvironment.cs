namespace QadAutomation.Core.Configuration;

/// <summary>
/// One fully-resolved deployment target, e.g. <c>pilot</c> / <c>PROD</c>.
/// </summary>
/// <remarks>
/// Every property here is already merged with the client's defaults and
/// validated, so consumers never have to ask "was this overridden?" or handle a
/// missing host. All the ambiguity lives in the raw config layer and is burned
/// off by <see cref="ConfigurationResolver"/> before this record is constructed.
/// </remarks>
/// <param name="Name">The canonical name, and the one every message reports by.</param>
/// <param name="IsProduction">Whether writing here needs an explicit confirmation.</param>
/// <param name="Ssh">Where and as whom to connect.</param>
/// <param name="Paths">Remote destinations per program kind.</param>
/// <param name="Compile">How each kind is built, if this environment can build.</param>
/// <param name="Aliases">
/// Other names this environment answers to on the command line.
/// </param>
public sealed record QadEnvironment(
    string Name,
    bool IsProduction,
    SshEndpoint Ssh,
    RemotePaths Paths,
    CompileSettings Compile,
    IReadOnlyList<string>? Aliases = null)
{
    /// <summary>
    /// Every name this environment answers to, the canonical one first.
    /// </summary>
    /// <remarks>
    /// Clients do not share vocabulary. One site's production is "euro" in every
    /// command and conversation there; another's non-production is "prototype".
    /// An alias lets an operator type the word they think in without the tool
    /// giving up the name it reports by.
    /// </remarks>
    public IReadOnlyList<string> Names => [Name, .. Aliases ?? []];

    /// <summary>Whether <paramref name="name"/> refers to this environment.</summary>
    /// <remarks>
    /// Case-insensitive, matching how environment names have always been
    /// compared - <c>prod</c> and <c>PROD</c> are the same environment.
    /// </remarks>
    public bool Answers(string name) =>
        Names.Any(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The canonical name, with any aliases after it, for display.</summary>
    public string Described =>
        Aliases is { Count: > 0 } aliases ? $"{Name} ({string.Join(", ", aliases)})" : Name;
}
