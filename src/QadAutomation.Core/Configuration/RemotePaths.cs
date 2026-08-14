namespace QadAutomation.Core.Configuration;

/// <summary>
/// The remote destination directory for each <see cref="ProgramKind"/> in one
/// environment.
/// </summary>
/// <remarks>
/// Both paths are optional because a client may legitimately never deploy one of
/// the two kinds. Rather than rejecting such a config up front, the path is
/// demanded only at the moment a file of that kind is actually about to be
/// uploaded - see <see cref="Require"/>. That keeps valid configurations working
/// while still making the failure loud and specific if a file turns up with
/// nowhere to go.
/// </remarks>
public sealed record RemotePaths(string? Src, string? Qrf)
{
    /// <summary>
    /// Returns the remote directory for <paramref name="kind"/>.
    /// </summary>
    /// <exception cref="ConfigurationException">
    /// Thrown when a file of this kind exists locally but the environment has no
    /// configured destination for it. Failing here - loudly, naming the missing
    /// setting - is far safer than defaulting to some other directory.
    /// </exception>
    public string Require(ProgramKind kind, string clientId, string environmentName)
    {
        var path = kind switch
        {
            ProgramKind.Src => Src,
            ProgramKind.Qrf => Qrf,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown program kind.")
        };

        if (string.IsNullOrWhiteSpace(path))
        {
            var setting = kind == ProgramKind.Src ? "srcRemotePath" : "qrfRemotePath";
            throw new ConfigurationException(
                $"Client '{clientId}', environment '{environmentName}' has {kind.ToString().ToUpperInvariant()} " +
                $"files to deploy but no '{setting}' is configured for it.");
        }

        return path;
    }
}
