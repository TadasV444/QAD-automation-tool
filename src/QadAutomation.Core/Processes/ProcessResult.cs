namespace QadAutomation.Core.Processes;

/// <summary>
/// What an external program left behind: its exit code and what it printed.
/// </summary>
/// <remarks>
/// Deliberately does <b>not</b> carry the arguments the program was launched
/// with. Those can contain a VPN password, and a result object is exactly the
/// kind of thing that ends up in a log line or an exception message by accident.
/// If it is not here, it cannot leak from here.
/// </remarks>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>Whether the program reported success.</summary>
    public bool Succeeded => ExitCode == 0;

    /// <summary>
    /// Both streams, in the order a human would expect to read them, with empty
    /// ones dropped. Console tools are inconsistent about which stream they use
    /// for errors, so for display purposes the distinction is rarely useful.
    /// </summary>
    public string CombinedOutput =>
        string.Join(
            Environment.NewLine,
            new[] { StandardOutput, StandardError }
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text.Trim()));
}
