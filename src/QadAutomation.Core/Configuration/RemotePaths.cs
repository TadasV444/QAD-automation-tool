using QadAutomation.Core.Tickets;

namespace QadAutomation.Core.Configuration;

/// <summary>
/// The remote destination directory for each <see cref="ProgramKind"/> in one
/// environment.
/// </summary>
/// <remarks>
/// <para>
/// Both paths are optional because a client may legitimately never deploy one of
/// the two kinds. Rather than rejecting such a config up front, the path is
/// demanded only at the moment a file of that kind is actually about to be
/// uploaded - see <see cref="Require"/>. That keeps valid configurations working
/// while still making the failure loud and specific if a file turns up with
/// nowhere to go.
/// </para>
/// <para>
/// A path may contain <c>{prefix}</c>, which is replaced per file by the
/// program's two-character prefix. One site keeps all SRC programs in a single
/// directory; another splits them into <c>.../src/us/xx</c>, <c>.../src/us/gp</c>
/// and so on. A placeholder covers both, where a second setting would have meant
/// every caller asking which style this client uses.
/// </para>
/// </remarks>
public sealed record RemotePaths(string? Src, string? Qrf)
{
    /// <summary>Marks the point in a path where the program's prefix belongs.</summary>
    public const string PrefixPlaceholder = "{prefix}";

    /// <summary>
    /// Returns the remote directory <paramref name="file"/> belongs in.
    /// </summary>
    /// <exception cref="ConfigurationException">
    /// Thrown when a file of this kind exists locally but the environment has no
    /// configured destination for it, or when the destination is per-prefix and
    /// the file's name has no prefix to use. Failing here - loudly, naming the
    /// setting - is far safer than defaulting to some other directory.
    /// </exception>
    public string Require(ProgramFile file, string clientId, string environmentName)
    {
        ArgumentNullException.ThrowIfNull(file);

        var path = file.Kind switch
        {
            ProgramKind.Src => Src,
            ProgramKind.Qrf => Qrf,
            _ => throw new ArgumentOutOfRangeException(nameof(file), file.Kind, "Unknown program kind.")
        };

        var setting = file.Kind == ProgramKind.Src ? "srcRemotePath" : "qrfRemotePath";

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ConfigurationException(
                $"Client '{clientId}', environment '{environmentName}' has {file.Kind.ToString().ToUpperInvariant()} " +
                $"files to deploy but no '{setting}' is configured for it.");
        }

        if (!path.Contains(PrefixPlaceholder, StringComparison.Ordinal))
        {
            return path;
        }

        if (file.Prefix is not { } prefix)
        {
            throw new ConfigurationException(
                $"'{file.FileName}' has no two-character prefix, but '{setting}' for client " +
                $"'{clientId}', environment '{environmentName}' needs one to choose a directory.");
        }

        return path.Replace(PrefixPlaceholder, prefix, StringComparison.Ordinal);
    }
}
