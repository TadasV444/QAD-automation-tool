using System.Text.Json.Nodes;
using QadAutomation.Core.Configuration;

namespace QadAutomation.Core.Tests.Configuration;

public sealed class VariableExpanderTests
{
    [Fact]
    public void A_placeholder_is_replaced_with_its_value()
    {
        var node = Expand("""{ "password": "${PW}" }""", ("PW", "hunter2"));

        Assert.Equal("hunter2", node["password"]!.GetValue<string>());
    }

    [Fact]
    public void Placeholders_are_expanded_at_any_depth()
    {
        // The whole point of walking the JSON tree: nesting depth is irrelevant,
        // and so is whether the field existed when this class was written.
        var node = Expand(
            """{ "clients": [ { "defaults": { "password": "${PW}" } } ] }""",
            ("PW", "hunter2"));

        Assert.Equal(
            "hunter2",
            node["clients"]![0]!["defaults"]!["password"]!.GetValue<string>());
    }

    [Fact]
    public void Placeholders_inside_arrays_of_strings_are_expanded()
    {
        var node = Expand("""{ "commands": ["compile ${SUFFIX}"] }""", ("SUFFIX", "-v"));

        Assert.Equal("compile -v", node["commands"]![0]!.GetValue<string>());
    }

    [Fact]
    public void Several_placeholders_in_one_value_are_all_replaced()
    {
        var node = Expand("""{ "host": "${A}.${B}.local" }""", ("A", "devl"), ("B", "corp"));

        Assert.Equal("devl.corp.local", node["host"]!.GetValue<string>());
    }

    [Fact]
    public void Text_around_a_placeholder_is_preserved()
    {
        var node = Expand("""{ "path": "/qad/${ENVNAME}/src" }""", ("ENVNAME", "test"));

        Assert.Equal("/qad/test/src", node["path"]!.GetValue<string>());
    }

    [Fact]
    public void Values_without_placeholders_are_untouched()
    {
        var node = Expand("""{ "host": "plain.local", "port": 22, "flag": true }""");

        Assert.Equal("plain.local", node["host"]!.GetValue<string>());
        Assert.Equal(22, node["port"]!.GetValue<int>());
        Assert.True(node["flag"]!.GetValue<bool>());
    }

    [Fact]
    public void A_missing_variable_is_an_error_naming_it()
    {
        // Never substitute blank. A typo in a variable name must not become a
        // confusing authentication failure against a client's live server.
        var message = Error("""{ "password": "${NOT_SET}" }""");

        Assert.Contains("NOT_SET", message);
    }

    [Fact]
    public void Every_missing_variable_is_listed_in_one_error()
    {
        var message = Error("""{ "a": "${MISSING_A}", "b": "${MISSING_B}" }""");

        Assert.Contains("MISSING_A", message);
        Assert.Contains("MISSING_B", message);
        Assert.Contains("2 variable(s)", message);
    }

    [Fact]
    public void The_same_missing_variable_used_twice_is_reported_once()
    {
        var message = Error("""{ "a": "${GONE}", "b": "${GONE}" }""");

        Assert.Contains("1 variable(s)", message);
    }

    [Fact]
    public void The_error_names_the_env_file_to_edit()
    {
        Assert.Contains(".env", Error("""{ "a": "${GONE}" }"""));
    }

    [Fact]
    public void A_secret_value_never_appears_in_an_error_message()
    {
        // One missing variable must not cause a resolved one to be echoed back.
        var message = Error("""{ "a": "${PRESENT}", "b": "${GONE}" }""", ("PRESENT", "hunter2"));

        Assert.DoesNotContain("hunter2", message);
    }

    [Fact]
    public void A_lone_dollar_sign_is_not_a_placeholder()
    {
        var node = Expand("""{ "command": "echo $HOME and 100$" }""");

        Assert.Equal("echo $HOME and 100$", node["command"]!.GetValue<string>());
    }

    // --- helpers ---------------------------------------------------------

    private static JsonNode Expand(string json, params (string Name, string Value)[] variables)
    {
        var node = JsonNode.Parse(json)!;
        VariableExpander.Expand(node, ToDictionary(variables), "config.json", ".env");
        return node;
    }

    private static string Error(string json, params (string Name, string Value)[] variables) =>
        Assert.Throws<ConfigurationException>(() => Expand(json, variables)).Message;

    private static Dictionary<string, string> ToDictionary((string Name, string Value)[] variables) =>
        variables.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
}
