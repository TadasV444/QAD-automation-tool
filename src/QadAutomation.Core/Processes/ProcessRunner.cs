using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace QadAutomation.Core.Processes;

/// <summary>
/// The real <see cref="IProcessRunner"/>, on top of <see cref="Process"/>.
/// </summary>
/// <remarks>
/// <para>
/// Three details here are not incidental:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Output is read asynchronously.</b> The naive version - wait for exit,
///     then read the streams - deadlocks as soon as a program writes more than
///     the pipe buffer holds, because it blocks writing while we block waiting.
///     Rare enough to pass every test and then hang in front of a client.
///   </item>
///   <item>
///     <b>Standard input is redirected and immediately closed.</b> Console tools
///     prompt when they want something we did not supply; <c>rasdial</c> asks for
///     a password if it is missing. A closed stdin turns "hangs forever" into an
///     instant, reportable failure.
///   </item>
///   <item>
///     <b>The timeout kills the whole process tree.</b> Killing only the parent
///     leaves orphaned children holding the pipes open.
///   </item>
/// </list>
/// </remarks>
public sealed class ProcessRunner : IProcessRunner
{
    /// <inheritdoc />
    public ProcessResult Run(string executablePath, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // ArgumentList, never a joined string: the framework applies the correct
        // Windows quoting rules per argument. Building the command line by hand
        // is how a password containing a quote or a space turns into two
        // arguments and a confusing authentication failure.
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();

        process.OutputDataReceived += (_, e) => Append(standardOutput, e.Data);
        process.ErrorDataReceived += (_, e) => Append(standardError, e.Data);

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new ProcessExecutionException($"Could not run '{executablePath}': {ex.Message}", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Nothing to send, and an open stdin is an invitation to hang.
        process.StandardInput.Close();

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            TryKill(process);

            throw new ProcessExecutionException(
                $"'{Path.GetFileName(executablePath)}' did not finish within " +
                $"{timeout.TotalSeconds:0} seconds and was stopped.");
        }

        // The parameterless overload additionally waits for the async readers to
        // drain. Without it the last lines of output are routinely missing.
        process.WaitForExit();

        return new ProcessResult(process.ExitCode, standardOutput.ToString(), standardError.ToString());
    }

    private static void Append(StringBuilder builder, string? line)
    {
        if (line is not null)
        {
            builder.AppendLine(line);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // It exited between the timeout and the kill, or the OS refused.
            // Either way the timeout is the story worth telling, not this.
        }
    }
}
