namespace QadAutomation.Core.Configuration;

/// <summary>
/// Finds <c>config.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Search order, first hit wins:
/// </para>
/// <list type="number">
///   <item>an explicit path passed with <c>--config</c>;</item>
///   <item>the <c>QAD_TOOL_CONFIG</c> environment variable;</item>
///   <item><c>config.json</c> in the current directory - the normal case when
///         working in the repository folder;</item>
///   <item><c>config.json</c> next to the executable - for a published build;</item>
///   <item><c>%APPDATA%\QadAutomationTool\config.json</c> - a machine-wide fallback.</item>
/// </list>
/// <para>
/// <b>Both</b> <c>config.json</c> and <c>.env</c> are git-ignored. They hold the
/// two different kinds of sensitive data - client structure and secrets
/// respectively - and neither may ever be committed. Because they now live inside
/// the working tree, that protection is a <c>.gitignore</c> rule rather than a
/// structural guarantee, so it is worth confirming with <c>git status</c> before
/// a first push. <c>%APPDATA%</c> remains supported for anyone who would rather
/// keep the files out of the tree entirely.
/// </para>
/// </remarks>
public sealed class ConfigurationLocator : IConfigurationLocator
{
    public const string EnvironmentVariableName = "QAD_TOOL_CONFIG";
    public const string ApplicationFolderName = "QadAutomationTool";
    public const string FileName = "config.json";

    private readonly string? _explicitPath;

    /// <param name="explicitPath">
    /// A path supplied by the operator (<c>--config</c>), or <c>null</c> to search.
    /// </param>
    public ConfigurationLocator(string? explicitPath = null) => _explicitPath = explicitPath;

    /// <summary>The usual location: the folder the tool is run from.</summary>
    public static string CurrentDirectoryPath => Path.Combine(Directory.GetCurrentDirectory(), FileName);

    /// <summary>Beside the published executable.</summary>
    public static string ExecutableFolderPath => Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>The machine-wide fallback, outside any repository.</summary>
    public static string AppDataPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ApplicationFolderName,
        FileName);

    /// <inheritdoc />
    public string Locate()
    {
        // An explicit path is an instruction, not a suggestion: if it is wrong we
        // must say so rather than quietly falling back to a different file. Using
        // the wrong config could mean uploading to the wrong client.
        if (!string.IsNullOrWhiteSpace(_explicitPath))
        {
            var full = Path.GetFullPath(_explicitPath);
            if (!File.Exists(full))
            {
                throw new ConfigurationException($"No configuration file at '{full}'.");
            }

            return full;
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            var full = Path.GetFullPath(fromEnvironment);
            if (!File.Exists(full))
            {
                throw new ConfigurationException(
                    $"{EnvironmentVariableName} points at '{full}', but no file exists there.");
            }

            return full;
        }

        foreach (var candidate in new[] { CurrentDirectoryPath, ExecutableFolderPath, AppDataPath })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new ConfigurationException(
            $"No configuration file found. Looked in:{Environment.NewLine}" +
            $"  - {CurrentDirectoryPath}{Environment.NewLine}" +
            $"  - {ExecutableFolderPath}{Environment.NewLine}" +
            $"  - {AppDataPath}{Environment.NewLine}" +
            $"Copy config.example.json to config.json and .env.example to .env, " +
            $"then fill them in. Or pass --config <path>.");
    }
}
