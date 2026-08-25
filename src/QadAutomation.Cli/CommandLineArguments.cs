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
    string? ConfigPath,
    IReadOnlySet<string> Flags)
{
    /// <summary>The first positional argument, or <c>null</c>.</summary>
    public string? Target => Argument(0);

    /// <summary>The <paramref name="index"/>th positional argument, or <c>null</c>.</summary>
    public string? Argument(int index) =>
        index >= 0 && index < Arguments.Count ? Arguments[index] : null;

    /// <summary>Whether a switch such as <c>--dry-run</c> was given.</summary>
    public bool HasFlag(string flag) => Flags.Contains(flag);
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
    /// The guided flow, and what bare <c>qad</c> resolves to.
    /// </summary>
    /// <remarks>
    /// Named here rather than written out at each use because two places depend
    /// on it agreeing: the default below, and the decision to hold the console
    /// open afterwards.
    /// </remarks>
    public const string MenuCommand = "menu";

    /// <summary>Show what would happen without changing anything.</summary>
    public const string DryRunFlag = "--dry-run";

    /// <summary>Confirm an action that would otherwise be refused.</summary>
    public const string YesFlag = "--yes";

    /// <summary>Upload without renaming the existing remote file out of the way.</summary>
    public const string NoBackupFlag = "--no-backup";

    /// <summary>
    /// Switches the parser accepts.
    /// </summary>
    /// <remarks>
    /// An allow-list, so a mistyped <c>--dryrun</c> is rejected rather than
    /// ignored. Silently dropping an unrecognised switch is how someone ends up
    /// believing they ran a dry run when they did not.
    /// </remarks>
    private static readonly HashSet<string> KnownFlags =
        new(StringComparer.OrdinalIgnoreCase) { DryRunFlag, YesFlag, NoBackupFlag };

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
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                return new CommandLineArguments("help", [], configPath, flags);
            }

            if (KnownFlags.Contains(arg))
            {
                flags.Add(arg.ToLowerInvariant());
                continue;
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

        // Nothing to do means the guided flow, not the usage text. That is what
        // a double-clicked shortcut passes, and an operator who opened the tool
        // that way wants to deploy, not to read. 'qad help' and '-h' still
        // print the usage.
        return new CommandLineArguments(command ?? MenuCommand, positional, configPath, flags);
    }
}
