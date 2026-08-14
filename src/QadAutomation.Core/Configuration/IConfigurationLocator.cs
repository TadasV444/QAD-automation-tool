namespace QadAutomation.Core.Configuration;

/// <summary>
/// Decides which file the configuration should be read from.
/// </summary>
/// <remarks>
/// Separated from reading and parsing because "where is the config?" is a policy
/// question that is likely to change (an installer might fix the location; a CI
/// run might use an environment variable), while "how is JSON parsed?" will not.
/// </remarks>
public interface IConfigurationLocator
{
    /// <summary>
    /// Returns the full path of the configuration file to use.
    /// </summary>
    /// <exception cref="ConfigurationException">
    /// Thrown when no configuration file can be found, with guidance on where to
    /// create one.
    /// </exception>
    string Locate();
}
