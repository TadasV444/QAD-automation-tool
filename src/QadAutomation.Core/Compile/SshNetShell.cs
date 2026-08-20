using System.Net.Sockets;
using System.Text;
using QadAutomation.Core.Configuration;
using QadAutomation.Core.Transfer;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace QadAutomation.Core.Compile;

/// <summary>
/// <see cref="ISshShell"/> over an SSH.NET shell stream.
/// </summary>
/// <remarks>
/// The terminal type is not incidental. Progress reads its key bindings from the
/// terminal definition, so the function-key sequences in
/// <see cref="ProgressKeys"/> are only correct for the type requested here. The
/// two are constants in the same file for that reason.
/// </remarks>
public sealed class SshNetShell : ISshShell
{
    /// <summary>
    /// How often the read loop looks for new output. Short enough not to add
    /// noticeable delay to a compile that finishes in under a second, long
    /// enough not to spin.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly SshClient _client;
    private readonly ShellStream _stream;

    internal SshNetShell(SshClient client, ShellStream stream)
    {
        _client = client;
        _stream = stream;
    }

    /// <inheritdoc />
    public void Send(string text)
    {
        try
        {
            _stream.Write(text);
            _stream.Flush();
        }
        catch (Exception ex) when (ex is SshException or IOException)
        {
            throw new TransferException($"The remote shell closed while sending input: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public string ReadUntilIdle(TimeSpan idleFor, TimeSpan timeout)
    {
        var received = new StringBuilder();
        var start = DateTime.UtcNow;
        var lastData = start;

        while (DateTime.UtcNow - start < timeout)
        {
            string chunk;

            try
            {
                chunk = _stream.Read();
            }
            catch (Exception ex) when (ex is SshException or IOException)
            {
                // The editor is killed by closing the channel, so a dropped
                // stream is an expected way for a session to end. Whatever was
                // read before it went is still worth returning.
                break;
            }

            if (chunk.Length > 0)
            {
                received.Append(chunk);
                lastData = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - lastData >= idleFor)
            {
                break;
            }
            else
            {
                Thread.Sleep(PollInterval);
            }
        }

        return received.ToString();
    }

    public void Dispose()
    {
        // Closing the channel is how the editor is left: it has no exit key at
        // this site, and the operator's own procedure is to close the window.
        // Never throws - disposal runs on the failure path too.
        try
        {
            _stream.Dispose();
        }
        catch (Exception)
        {
            // Abandoning the session anyway.
        }

        try
        {
            if (_client.IsConnected)
            {
                _client.Disconnect();
            }
        }
        catch (Exception)
        {
            // As above.
        }

        _client.Dispose();
    }
}

/// <inheritdoc cref="ISshShellFactory" />
public sealed class SshNetShellFactory : ISshShellFactory
{
    /// <summary>
    /// Terminal geometry. Wide and tall so the editor does not wrap or paginate
    /// a compile statement that names two absolute paths - a wrapped line is
    /// still typed correctly, but the captured screen becomes much harder for an
    /// operator to read when something has gone wrong.
    /// </summary>
    private const uint Columns = 200;

    private const uint Rows = 50;

    /// <inheritdoc />
    public ISshShell Open(SshEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var client = new SshClient(BuildConnectionInfo(endpoint));

        try
        {
            client.Connect();

            var stream = client.CreateShellStream(
                ProgressKeys.TerminalType, Columns, Rows, 800, 600, 8192);

            return new SshNetShell(client, stream);
        }
        catch (SshAuthenticationException ex)
        {
            client.Dispose();
            throw new TransferException(
                $"'{endpoint.Username}' was refused by {endpoint.Host}. " +
                "Check the username and password in .env.",
                ex);
        }
        catch (Exception ex) when (ex is SocketException or SshOperationTimeoutException or SshConnectionException)
        {
            client.Dispose();
            throw new TransferException(
                $"Could not open a shell on {endpoint.Host}:{endpoint.Port}. " +
                "Is the VPN connected? Try 'qad vpn status <client>'.",
                ex);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static ConnectionInfo BuildConnectionInfo(SshEndpoint endpoint)
    {
        AuthenticationMethod authentication;

        if (endpoint.UsesKeyAuthentication)
        {
            try
            {
                authentication = new PrivateKeyAuthenticationMethod(
                    endpoint.Username, new PrivateKeyFile(endpoint.PrivateKeyPath!));
            }
            catch (Exception ex) when (ex is IOException or SshException)
            {
                throw new TransferException(
                    $"Could not read the private key at '{endpoint.PrivateKeyPath}': {ex.Message}", ex);
            }
        }
        else
        {
            authentication = new PasswordAuthenticationMethod(
                endpoint.Username, endpoint.Password ?? string.Empty);
        }

        return new ConnectionInfo(endpoint.Host, endpoint.Port, endpoint.Username, authentication)
        {
            Timeout = SshNetSftpSessionFactory.ConnectTimeout
        };
    }
}
