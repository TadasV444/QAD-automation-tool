using QadAutomation.Core.Configuration;

namespace QadAutomation.Core.Transfer;

/// <summary>
/// A connected SFTP session, reduced to the four operations this tool performs.
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
/// </remarks>
public interface ISftpSession : IDisposable
{
    /// <summary>The server's host key fingerprint, for the operator to eyeball.</summary>
    string HostKeyFingerprint { get; }

    /// <summary>Whether a file or directory exists at <paramref name="path"/>.</summary>
    bool Exists(string path);

    /// <summary>Renames a remote file. Used to take a backup before overwriting.</summary>
    void Rename(string fromPath, string toPath);

    /// <summary>Writes a local file to <paramref name="remotePath"/>, overwriting.</summary>
    void Upload(string localPath, string remotePath);

    /// <summary>Names of the entries in a remote directory.</summary>
    IReadOnlyList<string> List(string remoteDirectory);
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
