namespace QadAutomation.Core.Vpn;

/// <summary>
/// The standard <see cref="IVpnSession"/>, shared by every connector.
/// </summary>
/// <remarks>
/// The bookkeeping - did we open it, has it been disposed, was it detached - is
/// identical whatever the VPN vendor, so it lives here once. A connector supplies
/// only the part that differs: the callback that actually disconnects.
/// </remarks>
public sealed class VpnSession : IVpnSession
{
    private readonly Action? _disconnect;
    private bool _keepOpen;
    private bool _disposed;

    private VpnSession(string connectionName, bool openedByTool, Action? disconnect)
    {
        ConnectionName = connectionName;
        OpenedByTool = openedByTool;
        _disconnect = disconnect;
    }

    /// <inheritdoc />
    public string ConnectionName { get; }

    /// <inheritdoc />
    public bool OpenedByTool { get; }

    /// <summary>
    /// A connection this run established. Disposing takes it back down.
    /// </summary>
    public static VpnSession Opened(string connectionName, Action disconnect)
    {
        ArgumentNullException.ThrowIfNull(disconnect);
        return new VpnSession(connectionName, openedByTool: true, disconnect);
    }

    /// <summary>
    /// A connection that was already up, or one no connector manages. Disposing
    /// does nothing - there is no prior state to restore.
    /// </summary>
    public static VpnSession Adopted(string connectionName) =>
        new(connectionName, openedByTool: false, disconnect: null);

    /// <inheritdoc />
    public void KeepOpen() => _keepOpen = true;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_keepOpen || !OpenedByTool)
        {
            return;
        }

        _disconnect?.Invoke();
    }
}
