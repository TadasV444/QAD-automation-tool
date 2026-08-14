namespace QadAutomation.Core.Configuration;

/// <summary>
/// Where and how to open an SSH/SFTP connection for one environment.
/// </summary>
/// <remarks>
/// Both the upload step and the compile step talk to the same host with the same
/// credentials, so this is modelled once and shared rather than duplicated into a
/// separate "sftp" and "ssh" block in the config file.
/// </remarks>
public sealed record SshEndpoint(
    string Host,
    int Port,
    string Username,
    string? Password,
    string? PrivateKeyPath)
{
    /// <summary>
    /// True when a private key was configured. Key authentication is preferred
    /// over a password: it keeps the secret out of the config file entirely.
    /// </summary>
    public bool UsesKeyAuthentication => !string.IsNullOrWhiteSpace(PrivateKeyPath);
}
