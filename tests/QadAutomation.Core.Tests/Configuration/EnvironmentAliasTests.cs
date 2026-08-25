using QadAutomation.Core.Configuration;

namespace QadAutomation.Core.Tests.Configuration;

/// <summary>
/// Environments answering to the site's own word as well as the configured one.
/// </summary>
/// <remarks>
/// Clients do not share vocabulary: one site's production is "euro" in every
/// command and conversation there. An alias lets the operator type what they
/// think in, while the tool keeps reporting by one name.
/// </remarks>
public sealed class EnvironmentAliasTests
{
    [Fact]
    public void An_environment_answers_to_its_alias()
    {
        Assert.Equal("PROD", Client().RequireEnvironment("euro").Name);
    }

    [Fact]
    public void An_alias_is_matched_however_it_was_typed()
    {
        // Names have always been case-insensitive; aliases must not be stricter,
        // or the two would behave differently for no reason an operator could
        // guess at.
        Assert.Equal("PROD", Client().RequireEnvironment("EURO").Name);
    }

    [Fact]
    public void The_canonical_name_is_what_comes_back_out()
    {
        // Two names in, one name out. Otherwise the logs of two people doing the
        // same deploy would not match, and neither would their undo lines.
        var environment = Client().RequireEnvironment("euro");

        Assert.Equal("PROD", environment.Name);
        Assert.True(environment.IsProduction);
    }

    [Fact]
    public void An_unknown_name_lists_the_aliases_too()
    {
        // The likeliest cause of landing here is using a word the tool does not
        // know - so the message has to show every word it does.
        var message = Assert.Throws<ConfigurationException>(
            () => Client().RequireEnvironment("live")).Message;

        Assert.Contains("PROD (euro)", message, StringComparison.Ordinal);
        Assert.Contains("TEST", message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_environment_without_aliases_is_described_by_its_name_alone()
    {
        Assert.Equal("TEST", Client().RequireEnvironment("TEST").Described);
    }

    private static ClientProfile Client() =>
        new(
            "pilot",
            "Pilot Client",
            new VpnSettings(VpnType.None, null, null, null),
            [
                Environment("TEST", isProduction: false),
                Environment("PROD", isProduction: true, "euro")
            ]);

    private static QadEnvironment Environment(string name, bool isProduction, params string[] aliases) =>
        new(
            name,
            isProduction,
            new SshEndpoint("qad.example", 22, "mfg", "hunter2", null),
            new RemotePaths("/qad/src", "/qad/qrf"),
            new CompileSettings(null, null),
            aliases);
}
