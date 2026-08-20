using System.Globalization;
using System.Net.Sockets;
using System.Text;
using QadAutomation.Core.Configuration;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace QadAutomation.Core.Transfer;

/// <summary>
/// <see cref="ISftpSession"/> over SSH.NET.
/// </summary>
/// <remarks>
/// The only file in the tool that knows SSH.NET exists. Everything above it sees
/// four methods and a fingerprint, so replacing the library - or adding FTPS for
/// a site that does not offer SFTP - would change this file and its factory and
/// nothing else.
/// </remarks>
public sealed class SshNetSftpSession : ISftpSession
{
    private readonly SftpClient _client;

    internal SshNetSftpSession(SftpClient client, string hostKeyFingerprint)
    {
        _client = client;
        HostKeyFingerprint = hostKeyFingerprint;
    }

    /// <inheritdoc />
    public string HostKeyFingerprint { get; }

    /// <inheritdoc />
    public bool Exists(string path) => Guard(() => _client.Exists(path), $"check '{path}'");

    /// <inheritdoc />
    public void Download(string remotePath, string localPath) =>
        Guard(
            () =>
            {
                // Create, not OpenWrite: a shorter file written over a longer one
                // would otherwise keep the old tail and produce a backup that is
                // two versions spliced together.
                using var stream = File.Create(localPath);
                _client.DownloadFile(remotePath, stream);
            },
            $"download '{remotePath}'");

    /// <inheritdoc />
    public void Upload(string localPath, string remotePath) =>
        Guard(
            () =>
            {
                using var stream = File.OpenRead(localPath);

                // canOverride: true - the backup was already taken by the caller,
                // so refusing here would just fail the second run of a deploy.
                _client.UploadFile(stream, remotePath, canOverride: true);
            },
            $"upload '{Path.GetFileName(localPath)}' to '{remotePath}'");

    /// <inheritdoc />
    public void WriteText(string remotePath, string contents) =>
        Guard(
            () =>
            {
                // Newlines are written as-is. The manifest is read by a script on
                // a Unix host, so a Windows CRLF would leave a stray carriage
                // return on every filename - and the compile would look for
                // programs that do not exist while reporting nothing useful.
                using var stream = new MemoryStream(Encoding.ASCII.GetBytes(contents));
                _client.UploadFile(stream, remotePath, canOverride: true);
            },
            $"write '{remotePath}'");

    /// <inheritdoc />
    public IReadOnlyList<string> List(string remoteDirectory) =>
        Guard(
            () => _client.ListDirectory(remoteDirectory)
                .Select(entry => entry.Name)
                .Where(name => name is not ("." or ".."))
                .Order(StringComparer.Ordinal)
                .ToList(),
            $"list '{remoteDirectory}'");

    /// <inheritdoc />
    public DateTimeOffset? LastWriteTime(string remotePath) =>
        Guard<DateTimeOffset?>(
            () => _client.Exists(remotePath)
                ? new DateTimeOffset(_client.Get(remotePath).LastWriteTimeUtc, TimeSpan.Zero)
                : null,
            $"read the timestamp of '{remotePath}'");

    public void Dispose()
    {
        // Never throws: disposal runs on the failure path too, and an error here
        // would replace the real problem with a misleading one.
        try
        {
            if (_client.IsConnected)
            {
                _client.Disconnect();
            }
        }
        catch (Exception)
        {
            // Nothing useful to do - the session is being abandoned anyway.
        }

        _client.Dispose();
    }

    /// <summary>
    /// Turns SSH.NET's exception zoo into one operator-facing type.
    /// </summary>
    /// <remarks>
    /// SSH.NET signals a missing directory, a permissions problem and a dropped
    /// connection with three unrelated exception types, none of which mention
    /// what the tool was trying to do. Naming the operation is what makes the
    /// difference between "SftpPathNotFoundException" and "could not upload
    /// x.p to /appl/... - no such directory".
    /// </remarks>
    private static T Guard<T>(Func<T> operation, string description)
    {
        try
        {
            return operation();
        }
        catch (SftpPathNotFoundException ex)
        {
            throw new TransferException($"Could not {description}: no such file or directory.", ex);
        }
        catch (SftpPermissionDeniedException ex)
        {
            throw new TransferException($"Could not {description}: permission denied.", ex);
        }
        catch (SshConnectionException ex)
        {
            throw new TransferException($"Could not {description}: the connection dropped. {ex.Message}", ex);
        }
        catch (SshException ex)
        {
            throw new TransferException($"Could not {description}: {ex.Message}", ex);
        }
        catch (IOException ex)
        {
            throw new TransferException($"Could not {description}: {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            // Download writes to the local disk, so a local permissions problem
            // can surface here. Without this it would escape as an unhandled
            // exception and exit 99 instead of naming the failing operation.
            throw new TransferException($"Could not {description}: {ex.Message}", ex);
        }
    }

    private static void Guard(Action operation, string description) =>
        Guard<object?>(
            () =>
            {
                operation();
                return null;
            },
            description);
}

/// <inheritdoc cref="ISftpSessionFactory" />
public sealed class SshNetSftpSessionFactory : ISftpSessionFactory
{
    /// <summary>
    /// Long enough to cross a VPN, short enough that a wrong host does not
    /// look like a hang.
    /// </summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public ISftpSession Connect(SshEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var connectionInfo = BuildConnectionInfo(endpoint);
        var client = new SftpClient(connectionInfo) { OperationTimeout = TimeSpan.FromMinutes(5) };

        var fingerprint = string.Empty;

        client.HostKeyReceived += (_, e) =>
        {
            fingerprint = string.Create(
                CultureInfo.InvariantCulture,
                $"{e.HostKeyName} SHA256:{e.FingerPrintSHA256}");

            // Accepted unconditionally, and that is a known gap rather than an
            // oversight - see information.md, "Deferred". Pinning the key needs
            // somewhere to remember it, which is a design decision of its own.
            // Surfacing the fingerprint at least lets the operator notice a
            // change. Note the traffic is already inside the client's VPN.
            e.CanTrust = true;
        };

        try
        {
            client.Connect();
        }
        catch (SshAuthenticationException ex)
        {
            client.Dispose();
            throw new TransferException(
                $"'{endpoint.Username}' was refused by {endpoint.Host}. " +
                (endpoint.UsesKeyAuthentication
                    ? $"Check the key at '{endpoint.PrivateKeyPath}'."
                    : "Check the username and password in .env."),
                ex);
        }
        catch (Exception ex) when (ex is SocketException or SshOperationTimeoutException or SshConnectionException)
        {
            client.Dispose();
            throw new TransferException(
                $"Could not reach {endpoint.Host}:{endpoint.Port}. " +
                "Is the VPN connected? Try 'qad vpn status <client>'.",
                ex);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        return new SshNetSftpSession(client, fingerprint);
    }

    private static ConnectionInfo BuildConnectionInfo(SshEndpoint endpoint)
    {
        AuthenticationMethod authentication;

        if (endpoint.UsesKeyAuthentication)
        {
            try
            {
                var key = new PrivateKeyFile(endpoint.PrivateKeyPath!);
                authentication = new PrivateKeyAuthenticationMethod(endpoint.Username, key);
            }
            catch (Exception ex) when (ex is IOException or SshException)
            {
                throw new TransferException(
                    $"Could not read the private key at '{endpoint.PrivateKeyPath}': {ex.Message}", ex);
            }
        }
        else
        {
            authentication = new PasswordAuthenticationMethod(endpoint.Username, endpoint.Password ?? string.Empty);
        }

        return new ConnectionInfo(endpoint.Host, endpoint.Port, endpoint.Username, authentication)
        {
            Timeout = ConnectTimeout
        };
    }
}
