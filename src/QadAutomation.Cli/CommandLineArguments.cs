namespace QadAutomation.Cli;

/// <summary>
/// The parsed command line.
/// </summary>
public sealed record CommandLineArguments(
    string Command,
    string? Target,
    string? ConfigPath);

/// <summary>
/// A deliberately tiny hand-rolled argument parser.
/// </summary>
/// <remarks>
/// <para>
/// The tool currently has three commands and one option. Taking a dependency on
/// a parsing library to handle that would be more code to configure than to
/// write, and it is trivially replaceable later: everything above this class sees
/// only <see cref="CommandLineArguments"/>, so swapping in
/// <c>System.CommandLine</c> when the verb list grows changes this file alone.
/// </para>
/// </remarks>
public static class CommandLineParser
{
    public const string ConfigOption = "--config";

    /// <summary>
    /// Parses <paramref name="args"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// On unknown options or a missing option value - reported to the operator as
    /// a usage error, not a crash.
    /// </exception>
    public static CommandLineArguments Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new CommandLineArguments("help", null, null);
        }

        string? command = null;
        string? target = null;
        string? configPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (string.Equals(arg, ConfigOption, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException($"{ConfigOption} requires a path.");
                }

                configPath = args[++i];
                continue;
            }

            if (arg is "-h" or "--help" or "-?")
            {
                return new CommandLineArguments("help", null, configPath);
            }

            if (arg.StartsWith('-'))
            {
                throw new ArgumentException($"Unknown option '{arg}'.");
            }

            // First bare word is the verb, second is its argument. Anything more
            // is a mistake worth reporting rather than ignoring.
            if (command is null)
            {
                command = arg;
            }
            else if (target is null)
            {
                target = arg;
            }
            else
            {
                throw new ArgumentException($"Unexpected argument '{arg}'.");
            }
        }

        return new CommandLineArguments(command ?? "help", target, configPath);
    }
}
