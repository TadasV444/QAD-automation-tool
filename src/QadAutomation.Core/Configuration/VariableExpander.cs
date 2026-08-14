using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace QadAutomation.Core.Configuration;

/// <summary>
/// Replaces <c>${VARIABLE}</c> placeholders in the configuration with values from
/// <c>.env</c> and the process environment.
/// </summary>
/// <remarks>
/// <para>
/// This is what lets <c>config.json</c> hold structure while <c>.env</c> holds
/// secrets:
/// </para>
/// <code>
/// config.json   "password": "${PILOT_SSH_PASSWORD}"
/// .env          PILOT_SSH_PASSWORD=hunter2
/// </code>
/// <para>
/// <b>Why this walks the JSON tree instead of the C# model.</b> The obvious
/// implementation expands each known field - <c>Password</c>, <c>Username</c> and
/// so on - by hand. That version has a bad failure mode: add a field to the raw
/// model later, forget to add it here, and a <c>${...}</c> placeholder in that
/// field is silently used as a literal value. A password of the literal text
/// "${PILOT_SSH_PASSWORD}" would then be sent to a live server.
/// </para>
/// <para>
/// Walking the parsed JSON means <i>every</i> string value in the file is
/// expanded, including fields that do not exist yet. There is nothing to
/// remember and nothing to forget.
/// </para>
/// <para>
/// A missing variable is always an error, never an empty string. Substituting
/// blank for an absent password would turn a typo in a variable name into a
/// confusing authentication failure against a client's server; naming the
/// variable turns it into a five-second fix.
/// </para>
/// </remarks>
public static partial class VariableExpander
{
    /// <summary>
    /// Matches <c>${NAME}</c>, where NAME follows shell variable naming rules.
    /// Source-generated, so the pattern is compiled at build time.
    /// </summary>
    [GeneratedRegex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    /// <summary>
    /// Expands every placeholder in <paramref name="node"/>, in place.
    /// </summary>
    /// <param name="node">The parsed configuration document.</param>
    /// <param name="variables">Values from <c>.env</c> plus the process environment.</param>
    /// <param name="configPath">Only used to make the error message point somewhere.</param>
    /// <param name="envPath">Only used to tell the operator which file to edit.</param>
    /// <exception cref="ConfigurationException">
    /// If any placeholder has no matching variable. All of them are listed at once.
    /// </exception>
    public static void Expand(
        JsonNode node,
        IReadOnlyDictionary<string, string> variables,
        string configPath,
        string envPath)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(variables);

        // A sorted set so the same missing variable referenced by three
        // environments is reported once, and the order is stable.
        var missing = new SortedSet<string>(StringComparer.Ordinal);

        ExpandNode(node, variables, missing);

        if (missing.Count > 0)
        {
            throw new ConfigurationException(
                $"'{configPath}' references {missing.Count} variable(s) that are not defined " +
                $"in '{envPath}' or the environment:{Environment.NewLine}" +
                string.Join(Environment.NewLine, missing.Select(name => "  - " + name)));
        }
    }

    /// <summary>
    /// Depth-first walk. Only string values are touched; numbers and booleans
    /// cannot contain a placeholder.
    /// </summary>
    private static void ExpandNode(
        JsonNode? node,
        IReadOnlyDictionary<string, string> variables,
        ISet<string> missing)
    {
        switch (node)
        {
            case JsonObject obj:
                // ToList() because assigning to an indexer while enumerating the
                // live object would invalidate the enumerator.
                foreach (var (key, child) in obj.ToList())
                {
                    if (child is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        obj[key] = Substitute(text, variables, missing);
                    }
                    else
                    {
                        ExpandNode(child, variables, missing);
                    }
                }

                break;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        array[i] = Substitute(text, variables, missing);
                    }
                    else
                    {
                        ExpandNode(array[i], variables, missing);
                    }
                }

                break;
        }
    }

    /// <summary>
    /// Replaces every placeholder in one string, recording any that cannot be
    /// resolved rather than throwing on the first.
    /// </summary>
    private static string Substitute(
        string text,
        IReadOnlyDictionary<string, string> variables,
        ISet<string> missing)
    {
        // Fast path: the overwhelming majority of values are plain literals.
        if (!text.Contains("${", StringComparison.Ordinal))
        {
            return text;
        }

        return PlaceholderPattern().Replace(text, match =>
        {
            var name = match.Groups[1].Value;

            if (variables.TryGetValue(name, out var value))
            {
                return value;
            }

            missing.Add(name);

            // Left as-is so the resulting document stays well-formed; the
            // exception thrown by Expand is what actually stops the run.
            return match.Value;
        });
    }
}
