namespace QadAutomation.Cli.Output;

/// <summary>
/// Renders secret values for human eyes without disclosing them.
/// </summary>
/// <remarks>
/// Every path that prints configuration goes through here. Centralising it means
/// "did we remember to redact this one?" has a single answer, and a future logger
/// or audit trail can reuse the same rule rather than inventing its own.
/// </remarks>
public static class SecretDisplay
{
    /// <summary>
    /// Shows whether a secret is set, never what it is.
    /// </summary>
    /// <remarks>
    /// The length is not shown either. It leaks something for no operational
    /// benefit - knowing a password is configured is all anyone needs here.
    /// </remarks>
    public static string Describe(string? secret) =>
        string.IsNullOrEmpty(secret) ? "(not set)" : "(set)";
}
