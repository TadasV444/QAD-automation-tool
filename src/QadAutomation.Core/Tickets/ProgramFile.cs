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

    /// <summary>
    /// The program's two-character prefix, lower-cased, or <c>null</c> if its
    /// name has no room for one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A QAD convention: the first two characters group a program into a
    /// functional area - <c>xx</c> for customisations, and <c>gp ic l4 sf</c>
    /// and the rest alongside it. Both sites seen so far use it to pick a
    /// directory, one for compiled output and one for the source itself.
    /// </para>
    /// <para>
    /// Lives on the file rather than in either of those two callers because it
    /// is a property of the program's name, and having it derived twice is how
    /// upload and compile would come to disagree about where a file belongs.
    /// The valid prefixes are deliberately not enumerated anywhere: the servers
    /// have the directories, and a list in code could only go stale.
    /// </para>
    /// </remarks>
    public string? Prefix
    {
        get
        {
            var dot = FileName.IndexOf('.');
            var name = dot > 0 ? FileName[..dot] : FileName;

            return name.Length >= 2 && char.IsLetterOrDigit(name[0]) && char.IsLetterOrDigit(name[1])
                ? name[..2].ToLowerInvariant()
                : null;
        }
    }
}
