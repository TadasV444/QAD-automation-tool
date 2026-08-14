namespace QadAutomation.Core.Tickets;

/// <summary>
/// One local file staged for deployment, together with the kind it was
/// classified as.
/// </summary>
/// <param name="Kind">
/// Determined solely by which sub-folder the file was found in - never by its
/// extension or contents.
/// </param>
/// <param name="LocalPath">Absolute path on the engineer's machine.</param>
public sealed record ProgramFile(ProgramKind Kind, string LocalPath)
{
    /// <summary>The file name, which is also the name it will be given remotely.</summary>
    public string FileName => Path.GetFileName(LocalPath);
}
