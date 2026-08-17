namespace QadAutomation.Core.Transfer;

/// <summary>
/// A file could not be transferred, or the server could not be reached.
/// </summary>
/// <remarks>
/// The operator-facing failure type for step 3, alongside
/// <c>ConfigurationException</c> and <c>VpnException</c>. Printed without a
/// stack trace and mapped to its own exit code, because "the upload failed" is
/// a normal operational outcome rather than a defect in the tool.
/// </remarks>
public sealed class TransferException : Exception
{
    public TransferException(string message)
        : base(message)
    {
    }

    public TransferException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
