namespace QadAutomation.Core.Configuration;

/// <summary>
/// Raised when configuration is missing, malformed, or incomplete.
/// </summary>
/// <remarks>
/// A distinct exception type lets the CLI present these as a plain, actionable
/// message ("fix your config file, here is what is wrong") and exit cleanly,
/// instead of dumping a stack trace for what is nearly always operator error.
/// </remarks>
public sealed class ConfigurationException : Exception
{
    public ConfigurationException(string message) : base(message)
    {
    }

    public ConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Builds one exception describing every problem found, rather than only the
    /// first. Fixing a config file one error per run is needlessly slow.
    /// </summary>
    public static ConfigurationException FromErrors(string configPath, IReadOnlyList<string> errors)
    {
        var detail = string.Join(Environment.NewLine, errors.Select(e => "  - " + e));
        return new ConfigurationException(
            $"Configuration at '{configPath}' is not valid:{Environment.NewLine}{detail}");
    }
}
