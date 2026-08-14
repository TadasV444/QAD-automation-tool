using System.Text.Json;
using System.Text.Json.Nodes;
using QadAutomation.Core.Configuration.Raw;

namespace QadAutomation.Core.Configuration;

/// <summary>
/// Reads <c>config.json</c>, substitutes <c>${...}</c> secrets from <c>.env</c>,
/// and hands the result to the <see cref="ConfigurationResolver"/>.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline, in order:
/// </para>
/// <list type="number">
///   <item><see cref="IConfigurationLocator"/> decides which file;</item>
///   <item>the file is parsed into a <see cref="JsonNode"/> tree;</item>
///   <item><see cref="VariableExpander"/> fills in <c>${...}</c> from <c>.env</c>;</item>
///   <item>the tree is deserialised into the raw model;</item>
///   <item><see cref="ConfigurationResolver"/> merges defaults and validates.</item>
/// </list>
/// <para>
/// Expansion happens on the node tree, before deserialisation, so that every
/// string in the document is covered - see <see cref="VariableExpander"/> for why
/// that matters more than it sounds.
/// </para>
/// </remarks>
public sealed class JsonConfigurationLoader : IConfigurationLoader
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,

        // A key the tool does not recognise must not be silently ignored. If the
        // operator writes "srcPath" instead of "srcRemotePath" and we shrug, the
        // environment inherits a different destination and a file goes somewhere
        // unintended - the exact accident this tool exists to prevent.
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    private readonly IConfigurationLocator _locator;
    private readonly ConfigurationResolver _resolver;

    public JsonConfigurationLoader(IConfigurationLocator locator, ConfigurationResolver? resolver = null)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _resolver = resolver ?? new ConfigurationResolver();
    }

    /// <inheritdoc />
    public LoadedConfiguration Load()
    {
        var path = _locator.Locate();
        var json = ReadFile(path);

        // The .env sits next to config.json. Keeping them together means moving
        // the configuration somewhere else moves its secrets with it.
        var envPath = Path.Combine(
            Path.GetDirectoryName(path) ?? AppContext.BaseDirectory,
            EnvironmentFile.FileName);

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json, nodeOptions: null, DocumentOptions);
        }
        catch (JsonException ex)
        {
            throw new ConfigurationException($"Could not read '{path}': {ex.Message}", ex);
        }

        if (node is null)
        {
            throw new ConfigurationException($"'{path}' is empty.");
        }

        VariableExpander.Expand(node, EnvironmentFile.LoadWithProcessEnvironment(envPath), path, envPath);

        ConfigurationFile? file;
        try
        {
            file = node.Deserialize<ConfigurationFile>(SerializerOptions);
        }
        catch (JsonException ex)
        {
            // Covers unrecognised keys and values of the wrong type. The parser's
            // own message names the offending property.
            throw new ConfigurationException($"Could not read '{path}': {ex.Message}", ex);
        }

        if (file is null)
        {
            throw new ConfigurationException($"'{path}' is empty.");
        }

        return new LoadedConfiguration(_resolver.Resolve(file, path), path);
    }

    private static string ReadFile(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConfigurationException($"Could not read the configuration file at '{path}': {ex.Message}", ex);
        }
    }
}
