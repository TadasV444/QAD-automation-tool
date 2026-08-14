using QadAutomation.Core.Configuration;
using QadAutomation.Core.Configuration.Raw;

namespace QadAutomation.Core.Tests.Configuration;

/// <summary>
/// Tests for the merge-and-validate policy.
/// </summary>
/// <remarks>
/// Note the complete absence of mocks, fakes and setup ceremony: because the
/// resolver is pure, a test is just "here is an object, here is what should come
/// out". That property is the whole reason parsing, locating and resolving were
/// split into separate classes.
/// </remarks>
public sealed class ConfigurationResolverTests
{
    private readonly ConfigurationResolver _resolver = new();

    [Fact]
    public void Environment_inherits_values_from_client_defaults()
    {
        var file = FileWith(client => client.Environments =
        [
            new EnvironmentSection { Name = "DEVL" }
        ]);

        var environment = Resolve(file).Clients[0].Environments[0];

        Assert.Equal("default.host", environment.Ssh.Host);
        Assert.Equal("qad", environment.Ssh.Username);
        Assert.Equal("/qad/src", environment.Paths.Src);
    }

    [Fact]
    public void Environment_values_override_client_defaults()
    {
        var file = FileWith(client => client.Environments =
        [
            new EnvironmentSection
            {
                Name = "PROD",
                Host = "prod.host",
                SrcRemotePath = "/qad/prod/src"
            }
        ]);

        var environment = Resolve(file).Clients[0].Environments[0];

        Assert.Equal("prod.host", environment.Ssh.Host);
        Assert.Equal("/qad/prod/src", environment.Paths.Src);

        // Untouched fields must still come from the defaults.
        Assert.Equal("qad", environment.Ssh.Username);
        Assert.Equal("/qad/qrf", environment.Paths.Qrf);
    }

    [Fact]
    public void Port_defaults_to_22_when_not_configured()
    {
        var environment = Resolve(FileWith()).Clients[0].Environments[0];

        Assert.Equal(22, environment.Ssh.Port);
    }

    [Theory]
    [InlineData("PROD")]
    [InlineData("prod")]
    [InlineData("Production")]
    [InlineData("LIVE")]
    public void Production_is_inferred_from_the_environment_name(string name)
    {
        var file = FileWith(client => client.Environments = [new EnvironmentSection { Name = name }]);

        Assert.True(Resolve(file).Clients[0].Environments[0].IsProduction);
    }

    [Fact]
    public void Inferred_production_can_be_overridden_explicitly()
    {
        var file = FileWith(client => client.Environments =
        [
            new EnvironmentSection { Name = "PROD", IsProduction = false }
        ]);

        Assert.False(Resolve(file).Clients[0].Environments[0].IsProduction);
    }

    [Fact]
    public void Non_production_names_are_not_flagged()
    {
        var file = FileWith(client => client.Environments = [new EnvironmentSection { Name = "TEST" }]);

        Assert.False(Resolve(file).Clients[0].Environments[0].IsProduction);
    }

    [Fact]
    public void Missing_working_folder_is_an_error()
    {
        var file = FileWith();
        file.WorkingFolder = null;

        Assert.Contains("workingFolder", ResolveError(file));
    }

    [Fact]
    public void Missing_host_is_an_error_naming_the_environment()
    {
        var file = FileWith(client =>
        {
            client.Defaults!.Host = null;
            client.Environments = [new EnvironmentSection { Name = "DEVL" }];
        });

        var message = ResolveError(file);

        Assert.Contains("'host' is required", message);
        Assert.Contains("DEVL", message);
    }

    [Fact]
    public void An_environment_with_no_credentials_is_an_error()
    {
        var file = FileWith(client =>
        {
            client.Defaults!.Password = null;
            client.Defaults.PrivateKeyPath = null;
        });

        Assert.Contains("password", ResolveError(file));
    }

    [Fact]
    public void A_private_key_satisfies_the_credential_requirement()
    {
        var file = FileWith(client =>
        {
            client.Defaults!.Password = null;
            client.Defaults.PrivateKeyPath = @"C:\keys\qad.pem";
        });

        var environment = Resolve(file).Clients[0].Environments[0];

        Assert.True(environment.Ssh.UsesKeyAuthentication);
        Assert.Null(environment.Ssh.Password);
    }

    [Fact]
    public void Duplicate_client_ids_are_rejected()
    {
        var file = FileWith();
        file.Clients!.Add(file.Clients[0]);

        Assert.Contains("used more than once", ResolveError(file));
    }

    [Fact]
    public void Duplicate_environment_names_are_rejected()
    {
        var file = FileWith(client => client.Environments =
        [
            new EnvironmentSection { Name = "DEVL" },
            new EnvironmentSection { Name = "devl" }
        ]);

        Assert.Contains("defined more than once", ResolveError(file));
    }

    [Fact]
    public void Unknown_vpn_type_is_rejected_and_lists_the_valid_values()
    {
        var file = FileWith(client => client.Vpn = new VpnSection { Type = "OpenVpn" });

        var message = ResolveError(file);

        Assert.Contains("unknown vpn type", message);
        Assert.Contains("FortiClient", message);
    }

    [Fact]
    public void Windows_ras_without_a_connection_name_is_rejected()
    {
        var file = FileWith(client => client.Vpn = new VpnSection { Type = "WindowsRas" });

        Assert.Contains("connectionName", ResolveError(file));
    }

    [Fact]
    public void A_missing_vpn_block_means_the_tool_does_not_manage_the_vpn()
    {
        var file = FileWith(client => client.Vpn = null);

        Assert.Equal(VpnType.None, Resolve(file).Clients[0].Vpn.Type);
    }

    [Fact]
    public void Direct_command_compile_requires_at_least_one_command()
    {
        var file = FileWith(client => client.Defaults!.Compile =
            new CompileSection { Strategy = "DirectCommand", Commands = [] });

        Assert.Contains("at least one entry in 'commands'", ResolveError(file));
    }

    [Fact]
    public void Interactive_menu_compile_may_have_no_commands_yet()
    {
        var file = FileWith(client => client.Defaults!.Compile =
            new CompileSection { Strategy = "InteractiveMenu" });

        var compile = Resolve(file).Clients[0].Environments[0].Compile;

        Assert.Equal(CompileStrategy.InteractiveMenu, compile.Strategy);
        Assert.Empty(compile.Commands);
    }

    [Fact]
    public void Compile_is_replaced_wholesale_by_an_environment_not_merged()
    {
        var file = FileWith(client => client.Environments =
        [
            new EnvironmentSection
            {
                Name = "PROD",
                Compile = new CompileSection { Strategy = "InteractiveMenu" }
            }
        ]);

        var compile = Resolve(file).Clients[0].Environments[0].Compile;

        Assert.Equal(CompileStrategy.InteractiveMenu, compile.Strategy);

        // The default's commands must NOT leak into the overriding block.
        Assert.Empty(compile.Commands);
    }

    [Fact]
    public void Every_error_is_reported_in_one_pass()
    {
        var file = FileWith(client =>
        {
            client.Defaults!.Host = null;
            client.Defaults.Username = null;
        });
        file.WorkingFolder = null;

        var message = ResolveError(file);

        Assert.Contains("workingFolder", message);
        Assert.Contains("'host' is required", message);
        Assert.Contains("'username' is required", message);
    }

    [Fact]
    public void Display_name_falls_back_to_the_id()
    {
        var file = FileWith(client => client.DisplayName = null);

        Assert.Equal("pilot", Resolve(file).Clients[0].DisplayName);
    }

    // --- helpers ---------------------------------------------------------

    /// <summary>
    /// A minimal valid configuration, optionally mutated by the caller. Each test
    /// then changes only the one thing it is about, which keeps what is under
    /// test obvious.
    /// </summary>
    private static ConfigurationFile FileWith(Action<ClientSection>? customise = null)
    {
        var client = new ClientSection
        {
            Id = "pilot",
            DisplayName = "Pilot Client",
            Vpn = new VpnSection { Type = "WindowsRas", ConnectionName = "EXAMPLE-VPN" },
            Defaults = new EnvironmentSection
            {
                Host = "default.host",
                Username = "qad",
                Password = "secret",
                SrcRemotePath = "/qad/src",
                QrfRemotePath = "/qad/qrf",
                Compile = new CompileSection
                {
                    Strategy = "DirectCommand",
                    Commands = ["compile {remoteFile}"]
                }
            },
            Environments = [new EnvironmentSection { Name = "DEVL" }]
        };

        customise?.Invoke(client);

        return new ConfigurationFile
        {
            WorkingFolder = @"C:\QAD Tasks",
            Clients = [client]
        };
    }

    private ToolConfiguration Resolve(ConfigurationFile file) => _resolver.Resolve(file, "test.json");

    private string ResolveError(ConfigurationFile file) =>
        Assert.Throws<ConfigurationException>(() => _resolver.Resolve(file, "test.json")).Message;
}
