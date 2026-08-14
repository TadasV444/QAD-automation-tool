namespace QadAutomation.Core.Configuration;

/// <summary>
/// Supplies the application with validated configuration.
/// </summary>
/// <remarks>
/// The CLI depends on this interface rather than on
/// <see cref="JsonConfigurationLoader"/>, so swapping JSON for YAML - or handing
/// a test an in-memory configuration - changes nothing above this line.
/// </remarks>
public interface IConfigurationLoader
{
    /// <summary>Locates, parses and validates the configuration.</summary>
    /// <exception cref="ConfigurationException">If it is missing or invalid.</exception>
    LoadedConfiguration Load();
}

/// <summary>
/// The configuration plus the path it came from.
/// </summary>
/// <remarks>
/// The path travels with the configuration because the first question anyone asks
/// when the tool behaves unexpectedly is "which config file did it actually read?"
/// </remarks>
public sealed record LoadedConfiguration(ToolConfiguration Configuration, string SourcePath);
