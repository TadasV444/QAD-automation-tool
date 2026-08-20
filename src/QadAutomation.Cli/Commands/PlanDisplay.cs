using QadAutomation.Core.Compile;
using QadAutomation.Core.Configuration;
using QadAutomation.Core.Transfer;

namespace QadAutomation.Cli.Commands;

/// <summary>
/// How plans and outcomes are printed, in one place.
/// </summary>
/// <remarks>
/// Shared because <c>upload</c>, <c>compile</c> and <c>deploy</c> must describe
/// the same plan identically. If <c>deploy</c> printed its own version, the
/// screen an operator reads before a production run would be the one screen
/// nobody had checked against the others.
/// </remarks>
internal static class PlanDisplay
{
    public static void WriteHeader(
        TextWriter output,
        ClientProfile client,
        string ticketName,
        QadEnvironment environment)
    {
        output.WriteLine($"Ticket      : {ticketName}");
        output.WriteLine($"Client      : {client.DisplayName} [{client.Id}]");

        var marker = environment.IsProduction ? "   ** PRODUCTION **" : string.Empty;
        output.WriteLine($"Environment : {environment.Name}{marker}");
        output.WriteLine($"Server      : {environment.Ssh.Username}@{environment.Ssh.Host}:{environment.Ssh.Port}");
        output.WriteLine();
    }

    /// <summary>The files an upload will write, grouped by destination.</summary>
    /// <remarks>
    /// Grouped so a file about to go somewhere unexpected is obvious at a
    /// glance, rather than buried in a flat list.
    /// </remarks>
    public static void WriteUploadPlan(TextWriter output, UploadPlan plan)
    {
        if (plan.IsEmpty)
        {
            output.WriteLine("No SRC or QRF files found in this ticket folder.");
            return;
        }

        output.WriteLine($"{plan.Uploads.Count} file(s) to upload:");

        foreach (var group in plan.Uploads
                     .GroupBy(u => u.RemoteDirectory)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            output.WriteLine();
            output.WriteLine($"  {group.Key}/");

            foreach (var upload in group)
            {
                output.WriteLine($"    [{upload.Kind.ToString().ToUpperInvariant()}] {upload.File.FileName}");
            }
        }

        output.WriteLine();
    }

    /// <summary>The statements a compile will type, and what it will check.</summary>
    public static void WriteCompilePlan(TextWriter output, CompilePlan plan)
    {
        if (plan.Compiles.Count > 0)
        {
            output.WriteLine($"{plan.Compiles.Count} program(s) to compile:");
            output.WriteLine();

            foreach (var compile in plan.Compiles)
            {
                output.WriteLine($"  [{compile.Kind.ToString().ToUpperInvariant()}] {compile.File.FileName}");

                // Shown in full because it is what will actually be typed. A
                // wrong path here is the difference between compiling and
                // compiling the wrong thing.
                output.WriteLine($"      {compile.Statement}");
                output.WriteLine($"      -> {compile.RemoteResult}");
            }

            output.WriteLine();
        }

        if (plan.Skipped.Count == 0)
        {
            return;
        }

        output.WriteLine($"{plan.Skipped.Count} program(s) will NOT be compiled:");

        foreach (var skip in plan.Skipped)
        {
            output.WriteLine($"  [{skip.File.Kind.ToString().ToUpperInvariant()}] {skip.File.FileName}");
            output.WriteLine($"      {skip.Reason}");
        }

        output.WriteLine();
    }
}
