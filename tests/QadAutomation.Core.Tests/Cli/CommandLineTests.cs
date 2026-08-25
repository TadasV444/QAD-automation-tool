using QadAutomation.Cli;

namespace QadAutomation.Core.Tests.Cli;

public sealed class CommandLineParserTests
{
    [Fact]
    public void No_arguments_opens_the_guided_flow()
    {
        // What a double-clicked shortcut passes. Someone who opened the tool
        // that way came to deploy, not to read a usage screen.
        Assert.Equal("menu", CommandLineParser.Parse([]).Command);
    }

    [Fact]
    public void Help_is_still_reachable_by_asking_for_it()
    {
        Assert.Equal("help", CommandLineParser.Parse(["help"]).Command);
        Assert.Equal("help", CommandLineParser.Parse(["--help"]).Command);
        Assert.Equal("help", CommandLineParser.Parse(["-h"]).Command);
    }

    [Fact]
    public void A_command_and_its_target_are_parsed()
    {
        var parsed = CommandLineParser.Parse(["ticket", "9999555"]);

        Assert.Equal("ticket", parsed.Command);
        Assert.Equal("9999555", parsed.Target);
    }

    [Fact]
    public void The_config_option_is_parsed_from_anywhere_in_the_line()
    {
        var parsed = CommandLineParser.Parse(["--config", @"C:\c.json", "ticket", "9999555"]);

        Assert.Equal(@"C:\c.json", parsed.ConfigPath);
        Assert.Equal("ticket", parsed.Command);
        Assert.Equal("9999555", parsed.Target);
    }

    [Fact]
    public void The_config_option_requires_a_value()
    {
        Assert.Throws<ArgumentException>(() => CommandLineParser.Parse(["validate", "--config"]));
    }

    [Fact]
    public void An_unknown_option_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => CommandLineParser.Parse(["validate", "--verbose"]));
    }

    [Fact]
    public void Every_bare_word_after_the_verb_is_kept_in_order()
    {
        // The parser no longer judges how many arguments are too many - arity
        // belongs to the command. 'vpn connect pilot' needs two where
        // 'ticket' needs one.
        var parsed = CommandLineParser.Parse(["vpn", "connect", "pilot"]);

        Assert.Equal("vpn", parsed.Command);
        Assert.Equal(["connect", "pilot"], parsed.Arguments);
        Assert.Equal("connect", parsed.Target);
        Assert.Equal("pilot", parsed.Argument(1));
    }

    [Fact]
    public void Asking_for_an_argument_that_was_not_typed_gives_null()
    {
        Assert.Null(CommandLineParser.Parse(["validate"]).Argument(0));
    }
}

/// <summary>
/// End-to-end tests that drive the whole application through its entry point.
/// </summary>
/// <remarks>
/// Possible only because <see cref="CommandLineApplication"/> takes its output
/// streams as constructor arguments instead of writing to <c>Console</c>.
/// </remarks>
public sealed class CommandLineApplicationTests
{
    [Fact]
    public void Help_succeeds_and_documents_the_config_search_order()
    {
        var output = new StringWriter();

        var exitCode = new CommandLineApplication(output, new StringWriter()).Run(["help"]);

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Contains("QAD_TOOL_CONFIG", output.ToString());
    }

    [Fact]
    public void An_unknown_command_is_a_usage_error()
    {
        var error = new StringWriter();

        var exitCode = new CommandLineApplication(new StringWriter(), error).Run(["deploy-everything"]);

        Assert.Equal(ExitCode.UsageError, exitCode);
        Assert.Contains("Unknown command", error.ToString());
    }

    [Fact]
    public void Ticket_without_a_target_is_a_usage_error()
    {
        var exitCode = new CommandLineApplication(new StringWriter(), new StringWriter()).Run(["ticket"]);

        Assert.Equal(ExitCode.UsageError, exitCode);
    }

    [Fact]
    public void A_missing_config_file_exits_with_the_configuration_code_and_no_stack_trace()
    {
        var error = new StringWriter();

        var exitCode = new CommandLineApplication(new StringWriter(), error)
            .Run(["validate", "--config", Path.Combine(Path.GetTempPath(), "definitely-not-here.json")]);

        Assert.Equal(ExitCode.ConfigurationError, exitCode);
        Assert.DoesNotContain("   at ", error.ToString());
    }

    [Fact]
    public void Validate_prints_a_summary_without_revealing_secrets()
    {
        var folder = Path.Combine(Path.GetTempPath(), "qad-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var configPath = Path.Combine(folder, "config.json");
            File.WriteAllText(configPath,
                """
                {
                  "workingFolder": "C:\\QAD Tasks",
                  "clients": [{
                    "id": "pilot",
                    "vpn": { "type": "WindowsRas", "connectionName": "EXAMPLE-VPN", "password": "vpn-hunter2" },
                    "defaults": {
                      "host": "qad.example", "username": "qad", "password": "ssh-hunter2",
                      "srcRemotePath": "/qad/src",
                      "compile": { "qrf": { "editor": { "editorCommand": "compile_editor us test" } } }
                    },
                    "environments": [{ "name": "DEVL" }, { "name": "PROD" }]
                  }]
                }
                """);

            var output = new StringWriter();
            var exitCode = new CommandLineApplication(output, new StringWriter())
                .Run(["validate", "--config", configPath]);

            var text = output.ToString();

            Assert.Equal(ExitCode.Ok, exitCode);
            Assert.Contains("1 client(s), 2 environment(s)", text);
            Assert.Contains("** PRODUCTION **", text);

            // The whole point of SecretDisplay: neither secret may appear.
            Assert.DoesNotContain("hunter2", text);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
