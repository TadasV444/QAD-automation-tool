namespace QadAutomation.Core.Configuration;

/// <summary>
/// Reads a <c>.env</c> file into a dictionary of variables.
/// </summary>
/// <remarks>
/// <para>
/// A deliberately small dotenv parser - <c>KEY=value</c>, one per line. Taking a
/// package dependency for thirty lines of string handling is not a trade worth
/// making, and a hand-rolled version means no surprises about how a password
/// containing <c>#</c> or <c>=</c> is treated.
/// </para>
/// <para>
/// Supported:
/// </para>
/// <list type="bullet">
///   <item><c>KEY=value</c>, with optional <c>export</c> prefix;</item>
///   <item>blank lines and <c>#</c> comment lines;</item>
///   <item>single or double quotes around a value, stripped on read.</item>
/// </list>
/// <para>
/// Deliberately <b>not</b> supported: trailing comments after a value. In a file
/// whose entire purpose is passwords, treating everything after a <c>#</c> as a
/// comment would silently truncate any password containing one. The whole
/// remainder of the line is the value; wrap it in quotes if it has leading or
/// trailing spaces that matter.
/// </para>
/// </remarks>
public static class EnvironmentFile
{
    /// <summary>The conventional file name, expected next to <c>config.json</c>.</summary>
    public const string FileName = ".env";

    /// <summary>
    /// Reads <paramref name="path"/>, or returns an empty set if it is absent.
    /// </summary>
    /// <remarks>
    /// A missing <c>.env</c> is not an error here. A configuration that uses no
    /// <c>${...}</c> placeholders needs no <c>.env</c> at all; if one *is*
    /// referenced and cannot be resolved, that failure is raised later by
    /// <see cref="VariableExpander"/>, which can name the specific variable.
    /// </remarks>
    /// <exception cref="ConfigurationException">If the file exists but cannot be read or parsed.</exception>
    public static IReadOnlyDictionary<string, string> Load(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConfigurationException($"Could not read '{path}': {ex.Message}", ex);
        }

        // Ordinal, not OrdinalIgnoreCase: environment variable names are
        // case-sensitive by convention, and quietly matching PASSWORD to password
        // would be a surprise.
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].TrimStart();
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                throw new ConfigurationException(
                    $"'{path}' line {i + 1}: expected KEY=value but found '{lines[i].Trim()}'.");
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            variables[key] = Unquote(value);
        }

        return variables;
    }

    /// <summary>
    /// Combines a <c>.env</c> file with the real process environment.
    /// </summary>
    /// <remarks>
    /// Process environment variables win. That lets a value be overridden for one
    /// run - or supplied by a CI secret store that never writes a file - without
    /// editing anything on disk.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> LoadWithProcessEnvironment(string path)
    {
        var variables = new Dictionary<string, string>(Load(path), StringComparer.Ordinal);

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                variables[key] = value;
            }
        }

        return variables;
    }

    /// <summary>
    /// Strips one matching pair of surrounding quotes, so a value with meaningful
    /// leading or trailing spaces can be written as <c>KEY=" pw "</c>.
    /// </summary>
    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}
