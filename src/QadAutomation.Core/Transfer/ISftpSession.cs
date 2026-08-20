using QadAutomation.Core.Configuration;

namespace QadAutomation.Core.Transfer;

/// <summary>
/// A connected SFTP session, reduced to the operations this tool performs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this interface exists</b> - the same reasoning as <c>IProcessRunner</c>,
/// and the same standard applied. A real file system is cheap to create in a
/// test, so <c>IFileSystem</c> was rejected; a real SFTP server behind a client's
/// VPN is not, so the seam earns its place. Without it, "an existing remote file
/// is backed up before being overwritten" could only ever be verified by
/// overwriting a file on a client's server.
/// </para>
/// <para>
/// Deliberately narrow. Exposing SSH.NET's <c>SftpClient</c> directly would let
/// callers delete, chmod and recurse; this interface can only do what the upload
/// step needs, so no future caller can quietly start doing more.
/// </para>
/// <para>
/// Note there is no <c>Rename</c>. There was, when backups were taken by moving
/// the old file aside on the server; backups are now downloaded instead, and the
/// method went with the behaviour rather than being left available. Nothing the
/// tool cannot do is a thing it cannot do by accident.
/// </para>
/// </remarks>
public interface ISftpSession : IDisposable
{
    /// <summary>The server's host key fingerprint, for the operator to eyeball.</summary>
    string HostKeyFingerprint { get; }

    /// <summary>Whether a file or directory exists at <paramref name="path"/>.</summary>
    bool Exists(string path);

    /// <summary>
    /// Copies a remote file down to <paramref name="localPath"/>, overwriting it.
    /// </summary>
    /// <remarks>
    /// Used to take a backup before overwriting. The caller is responsible for
    /// the local directory existing - the local layout is its policy, not this
    /// session's.
    /// </remarks>
    void Download(string remotePath, string localPath);

    /// <summary>Writes a local file to <paramref name="remotePath"/>, overwriting.</summary>
    void Upload(string localPath, string remotePath);

    /// <summary>
    /// Writes <paramref name="contents"/> straight to <paramref name="remotePath"/>.
    /// </summary>
    /// <remarks>
    /// For the SRC compile manifest, which the tool composes rather than reads
    /// off disk. The alternative was writing a temporary local file and reusing
    /// <see cref="Upload"/>, which would mean a file on the operator's machine to
    /// clean up on every failure path in exchange for nothing.
    /// </remarks>
    void WriteText(string remotePath, string contents);

    /// <summary>Names of the entries in a remote directory.</summary>
    IReadOnlyList<string> List(string remoteDirectory);

    /// <summary>
    /// When <paramref name="remotePath"/> was last written, or <c>null</c> if it
    /// does not exist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is how a compile is verified. Progress leaves the old <c>.r</c>
    /// untouched when a compile fails, so comparing the timestamp either side of
    /// the attempt answers "did it actually build?" - the same check the operator
    /// does by eye today, and a far steadier signal than scraping an editor
    /// screen for error text.
    /// </para>
    /// <para>
    /// Both readings come from the server, so the client's clock is irrelevant
    /// and only the difference is ever used. Note SFTP reports whole seconds:
    /// two compiles of the same file inside one second would be
    /// indistinguishable. That is acceptable for an operator-driven tool and
    /// noted here so it is not rediscovered as a bug.
    /// </para>
    /// </remarks>
    DateTimeOffset? LastWriteTime(string remotePath);
}

/// <summary>
/// Opens <see cref="ISftpSession"/>s.
/// </summary>
public interface ISftpSessionFactory
{
    /// <summary>
    /// Connects and authenticates.
    /// </summary>
    /// <exception cref="TransferException">
    /// If the host is unreachable, the credentials are refused, or the path is
    /// not usable. The message names the cause in the operator's terms.
    /// </exception>
    ISftpSession Connect(SshEndpoint endpoint);
}
