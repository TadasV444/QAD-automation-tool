namespace QadAutomation.Cli;

/// <summary>
/// The parsed command line.
/// </summary>
/// <param name="Command">The verb - the first bare word.</param>
/// <param name="Arguments">Every bare word after the verb, in order.</param>
/// <param name="ConfigPath">The value of <c>--config</c>, if given.</param>
/// <remarks>
/// The positional arguments are kept as a list rather than as named fields
/// because arity is a property of each command, not of the command line:
/// <c>validate</c> takes none, <c>ticket</c> takes one, <c>vpn</c> takes two. The
/// parser's job is to say what was typed; deciding whether that is the right
/// amount belongs to the command that knows.
/// </remarks>
public sealed record CommandLineArguments(
    string Command,
    IReadOnlyList<string> Arguments,
    string? ConfigPath)
{
    /// <summary>The first positional argument, or <c>null</c>.</summary>
    public string? Target => Argument(0);

    /// <summary>The <paramref name="index"/>th positional argument, or <c>null</c>.</summary>
    public string? Argument(int index) =>
        index >= 0 && index < Arguments.Count ? Arguments[index] : null;
}

/// <summary>
/// A deliberately tiny hand-rolled argument parser.
/// </summary>
/// <remarks>
/// <para>
/// The tool has a handful of commands and one option. Taking a dependency on a
/// parsing library to handle that would be more code to configure than to write,
/// and it is trivially replaceable later: everything above this class sees only
/// <see cref="CommandLineArguments"/>, so swapping in <c>System.CommandLine</c>
/// when the verb list grows changes this file alone.
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
        ArgumentNullException.ThrowIfNull(args);

        string? command = null;
        var positional = new List<string>();
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
                return new CommandLineArguments("help", [], configPath);
            }

            if (arg.StartsWith('-'))
            {
                throw new ArgumentException($"Unknown option '{arg}'.");
            }

            if (command is null)
            {
                command = arg;
            }
            else
            {
                positional.Add(arg);
            }
        }

        return new CommandLineArguments(command ?? "help", positional, configPath);
    }
}
