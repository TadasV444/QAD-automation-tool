namespace QadAutomation.Core.Processes;

/// <summary>
/// Runs an external program to completion and captures its output.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is an interface when <c>IFileSystem</c> deliberately is not.</b>
/// The rule applied throughout this project is that an abstraction has to earn
/// its place. File access was left concrete because a test can create a real
/// temporary folder cheaply and the resulting test is more honest than one
/// against a mock.
/// </para>
/// <para>
/// Process execution fails that test. Verifying that a wrong password produces a
/// readable "check the credentials" message would otherwise require a real VPN,
/// real credentials and a real network - so in practice it would not be verified
/// at all. Here the seam buys something a real resource cannot: the ability to
/// reproduce every failure mode on demand, offline, in milliseconds.
/// </para>
/// </remarks>
public interface IProcessRunner
{
    /// <summary>
    /// Starts <paramref name="executablePath"/>, waits for it, and returns what it did.
    /// </summary>
    /// <param name="executablePath">Full path to the program.</param>
    /// <param name="arguments">
    /// Arguments as separate strings, <b>not</b> as one command line. Quoting is
    /// the implementation's problem, which is what keeps a connection name
    /// containing a space, or a password containing a quote, from silently
    /// changing the meaning of the command.
    /// </param>
    /// <param name="timeout">How long to wait before giving up and killing it.</param>
    /// <exception cref="ProcessExecutionException">
    /// If the program cannot be started, or does not finish within <paramref name="timeout"/>.
    /// </exception>
    ProcessResult Run(string executablePath, IReadOnlyList<string> arguments, TimeSpan timeout);
}
