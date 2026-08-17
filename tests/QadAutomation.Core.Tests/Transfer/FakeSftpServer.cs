using QadAutomation.Core.Configuration;
using QadAutomation.Core.Transfer;

namespace QadAutomation.Core.Tests.Transfer;

/// <summary>
/// An in-memory SFTP server: a set of directories and a map of file contents.
/// </summary>
/// <remarks>
/// Like <c>FakeRasDial</c>, this models the counterparty's behaviour rather than
/// recording expectations. Uploads really land somewhere, renames really move
/// them, and <see cref="Files"/> can be inspected afterwards - so a test can
/// assert that a backup exists and holds the <i>old</i> content, which is the
/// property that actually matters and which a mock could not express.
/// </remarks>
internal sealed class FakeSftpServer : ISftpSessionFactory, ISftpSession
{
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal);

    /// <summary>Remote path to contents.</summary>
    public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

    public SshEndpoint? ConnectedTo { get; private set; }

    public bool IsDisposed { get; private set; }

    /// <summary>Set to throw on connect, simulating bad credentials.</summary>
    public TransferException? ConnectFailure { get; set; }

    public string HostKeyFingerprint => "ssh-ed25519 SHA256:FAKEFAKEFAKE";

    public FakeSftpServer WithDirectory(params string[] paths)
    {
        foreach (var path in paths)
        {
            _directories.Add(path.TrimEnd('/'));
        }

        return this;
    }

    public FakeSftpServer WithFile(string path, string contents)
    {
        Files[path] = contents;
        return this;
    }

    // --- ISftpSessionFactory ---------------------------------------------

    public ISftpSession Connect(SshEndpoint endpoint)
    {
        if (ConnectFailure is not null)
        {
            throw ConnectFailure;
        }

        ConnectedTo = endpoint;
        return this;
    }

    // --- ISftpSession ----------------------------------------------------

    public bool Exists(string path) =>
        _directories.Contains(path.TrimEnd('/')) || Files.ContainsKey(path);

    public void Rename(string fromPath, string toPath)
    {
        if (!Files.Remove(fromPath, out var contents))
        {
            throw new TransferException($"Could not rename '{fromPath}': no such file.");
        }

        Files[toPath] = contents;
    }

    public void Upload(string localPath, string remotePath)
    {
        // Reads the real local file, so a test that forgets to create one fails
        // the same way the real uploader would.
        Files[remotePath] = File.ReadAllText(localPath);
    }

    public IReadOnlyList<string> List(string remoteDirectory)
    {
        var prefix = remoteDirectory.TrimEnd('/') + "/";

        return [.. Files.Keys
            .Where(p => p.StartsWith(prefix, StringComparison.Ordinal))
            .Select(p => p[prefix.Length..])
            .Where(name => !name.Contains('/', StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];
    }

    public void Dispose() => IsDisposed = true;
}
