namespace QadAutomation.Core.Compile;

/// <summary>What happened to one program.</summary>
public enum CompileResult
{
    /// <summary>The <c>.r</c> is newer than it was. The only evidence of success.</summary>
    Compiled,

    /// <summary>The <c>.r</c> did not move, so Progress rejected the source.</summary>
    Failed
}

/// <summary>
/// One program's result, with whatever the editor put on screen.
/// </summary>
/// <param name="Planned">What was attempted.</param>
/// <param name="Result">The verdict, taken from the <c>.r</c> timestamp.</param>
/// <param name="Screen">
/// Everything the editor printed for this file. Shown only on failure, and never
/// used to decide the verdict - it is an explanation, not evidence.
/// </param>
public sealed record CompiledProgram(PlannedCompile Planned, CompileResult Result, string Screen);

/// <summary>The result of a compile run.</summary>
public sealed record CompileOutcome(
    IReadOnlyList<CompiledProgram> Programs,
    IReadOnlyList<SkippedProgram> Skipped)
{
    public int CompiledCount => Programs.Count(p => p.Result == CompileResult.Compiled);

    public int FailedCount => Programs.Count(p => p.Result == CompileResult.Failed);

    public IReadOnlyList<CompiledProgram> Failures =>
        [.. Programs.Where(p => p.Result == CompileResult.Failed)];

    /// <summary>
    /// Whether the operator has anything to act on.
    /// </summary>
    /// <remarks>
    /// A skipped program counts. It is not a failure of the compile, but a
    /// ticket where SRC was silently left unbuilt is exactly the state this tool
    /// exists to prevent, so the run must not read as clean.
    /// </remarks>
    public bool NeedsAttention => FailedCount > 0 || Skipped.Count > 0;
}
