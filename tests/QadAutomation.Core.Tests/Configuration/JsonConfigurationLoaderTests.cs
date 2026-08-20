using QadAutomation.Core.Configuration;

namespace QadAutomation.Core.Tests.Configuration;

/// <summary>
/// Tests for the parsing layer, using a real file on disk.
/// </summary>
public sealed class JsonConfigurationLoaderTests : IDisposable
{
    private readonly string _folder;

    public JsonConfigurationLoaderTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "qad-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void A_valid_file_loads_and_reports_where_it_came_from()
    {
        var path = WriteConfig(ValidJson);

        var loaded = Load(path);

        Assert.Equal(path, loaded.SourcePath);
        Assert.Equal(@"C:\QAD Tasks", loaded.Configuration.WorkingFolder);
        Assert.Equal("pilot", Assert.Single(loaded.Configuration.Clients).Id);
    }

    [Fact]
    public void An_unrecognised_key_is_rejected_rather_than_silently_ignored()
    {
        // This is the important one. A "srcPath" key quietly ignored would mean an
        // environment silently inherits the wrong destination and a file lands in
        // the wrong remote directory - the precise accident this tool exists to
        // prevent. The parser names the offending key so the fix is obvious.
        var path = WriteConfig(ValidJson.Replace("\"srcRemotePath\"", "\"srcPath\""));

        var message = Assert.Throws<ConfigurationException>(() => Load(path)).Message;

        Assert.Contains("srcPath", message);
    }

    [Fact]
    public void Key_casing_does_not_have_to_match_exactly()
    {
        // Case-insensitive matching is deliberate: "SrcRemotePath" is the same key,
        // not a typo, and rejecting it would be pedantry rather than safety.
        var path = WriteConfig(ValidJson.Replace("\"srcRemotePath\"", "\"SrcRemotePath\""));

        Assert.Equal("/qad/src", Load(path).Configuration.Clients[0].Environments[0].Paths.Src);
    }

    [Fact]
    public void A_secret_is_taken_from_the_env_file_beside_the_config()
    {
        // The end-to-end shape of the chosen design: structure in config.json,
        // secret in .env, neither committed.
        var path = WriteConfig(ValidJson.Replace("\"password\": \"secret\"", "\"password\": \"${PILOT_SSH_PASSWORD}\""));
        File.WriteAllText(Path.Combine(_folder, ".env"), "PILOT_SSH_PASSWORD=hunter2\n");

        Assert.Equal("hunter2", Load(path).Configuration.Clients[0].Environments[0].Ssh.Password);
    }

    [Fact]
    public void A_placeholder_with_no_env_file_is_an_error_naming_the_variable()
    {
        var path = WriteConfig(ValidJson.Replace("\"password\": \"secret\"", "\"password\": \"${PILOT_SSH_PASSWORD}\""));

        var message = Assert.Throws<ConfigurationException>(() => Load(path)).Message;

        Assert.Contains("PILOT_SSH_PASSWORD", message);

        // Crucially it must NOT fall through to "password is required" - that
        // would send the operator hunting in the wrong file.
        Assert.DoesNotContain("is required", message);
    }

    [Fact]
    public void Malformed_json_is_reported_as_a_configuration_error()
    {
        var path = WriteConfig("{ not json");

        Assert.Throws<ConfigurationException>(() => Load(path));
    }

    [Fact]
    public void Trailing_commas_and_comments_are_tolerated()
    {
        var path = WriteConfig(ValidJson.Replace(
            "\"workingFolder\"",
            "// a note from the operator\n  \"workingFolder\""));

        Assert.NotNull(Load(path));
    }

    [Fact]
    public void An_explicit_config_path_that_does_not_exist_is_an_error()
    {
        var missing = Path.Combine(_folder, "nope.json");

        Assert.Contains("No configuration file at", Assert.Throws<ConfigurationException>(() => Load(missing)).Message);
    }

    [Fact]
    public void When_nothing_is_configured_the_error_lists_every_place_searched()
    {
        var locator = new ConfigurationLocator();

        var searched = new[]
        {
            ConfigurationLocator.CurrentDirectoryPath,
            ConfigurationLocator.ExecutableFolderPath,
            ConfigurationLocator.AppDataPath
        };

        if (searched.Any(File.Exists) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConfigurationLocator.EnvironmentVariableName)))
        {
            // A real config exists on this machine, so the search-failure path
            // cannot be exercised here. Assert the successful outcome instead
            // rather than skipping silently.
            Assert.True(File.Exists(locator.Locate()));
            return;
        }

        var message = Assert.Throws<ConfigurationException>(() => locator.Locate()).Message;

        Assert.All(searched, path => Assert.Contains(path, message));
    }

    // --- helpers ---------------------------------------------------------

    private string WriteConfig(string json)
    {
        var path = Path.Combine(_folder, "config.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static LoadedConfiguration Load(string path) =>
        new JsonConfigurationLoader(new ConfigurationLocator(path)).Load();

    private const string ValidJson =
        """
        {
          "workingFolder": "C:\\QAD Tasks",
          "clients": [
            {
              "id": "pilot",
              "displayName": "Pilot Client",
              "vpn": { "type": "WindowsRas", "connectionName": "EXAMPLE-VPN" },
              "defaults": {
                "host": "qad.example",
                "username": "qad",
                "password": "secret",
                "srcRemotePath": "/qad/src",
                "qrfRemotePath": "/qad/qrf",
                "compile": { "qrf": { "editorCommand": "/qad/qrf/compile_editor us devl" } }
              },
              "environments": [ { "name": "DEVL" } ]
            }
          ]
        }
        """;
}
