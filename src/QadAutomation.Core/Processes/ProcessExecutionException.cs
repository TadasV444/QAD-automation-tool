namespace QadAutomation.Core.Processes;

/// <summary>
/// An external program could not be started, or would not finish.
/// </summary>
/// <remarks>
/// Distinct from "the program ran and reported a failure", which is an ordinary
/// <see cref="ProcessResult"/> with a non-zero exit code. Only the caller knows
/// what a given exit code means; not being able to run the program at all is a
/// problem no caller can interpret differently.
/// </remarks>
public sealed class ProcessExecutionException : Exception
{
    public ProcessExecutionException(string message)
        : base(message)
    {
    }

    public ProcessExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
