using QadAutomation.Core.Tickets;

namespace QadAutomation.Core.Transfer;

/// <summary>
/// One local file and the exact remote path it will be written to.
/// </summary>
/// <remarks>
/// The destination is resolved once, here, rather than being recomputed at
/// upload time. That is what makes a dry run trustworthy: the paths printed by
/// <c>--dry-run</c> are the same strings the upload will use, not a second
/// derivation of them that could disagree.
/// </remarks>
public sealed record PlannedUpload(ProgramFile File, string RemoteDirectory)
{
    /// <summary>The full remote path, always with forward slashes.</summary>
    /// <remarks>
    /// Built by hand rather than with <c>Path.Combine</c>, which on
    /// Windows would produce a backslash and quietly create a file literally
    /// named <c>dir\file.p</c> on the server.
    /// </remarks>
    public string RemotePath => $"{RemoteDirectory.TrimEnd('/')}/{File.FileName}";

    /// <summary>The kind of program this is - decides which remote path applies.</summary>
    public ProgramKind Kind => File.Kind;
}
