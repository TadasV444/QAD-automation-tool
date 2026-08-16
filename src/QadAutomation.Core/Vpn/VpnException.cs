namespace QadAutomation.Core.Vpn;

/// <summary>
/// The VPN could not be brought up, taken down, or inspected.
/// </summary>
/// <remarks>
/// Like <c>ConfigurationException</c>, this is the "expected failure, aimed at
/// the operator" type: the message is the entire user experience, so it names
/// the connection and says what to do next rather than describing an internal
/// state. It is caught at the composition root and printed without a stack
/// trace.
/// </remarks>
public sealed class VpnException : Exception
{
    public VpnException(string message)
        : base(message)
    {
    }

    public VpnException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
