namespace QadAutomation.Core;

/// <summary>
/// The two kinds of QAD program the tool deploys.
/// </summary>
/// <remarks>
/// This is the single concept that ties the local folder layout to the remote
/// layout: a ticket folder contains an <c>SRC</c> and/or <c>QRF</c> sub-folder,
/// and each kind has its own remote destination per environment. The kind is
/// never inferred from a file extension - it is declared by the folder the file
/// was found in, so a file cannot be silently misrouted.
/// </remarks>
public enum ProgramKind
{
    /// <summary>Maintenance programs, found in the ticket's <c>SRC</c> folder.</summary>
    Src,

    /// <summary>Reporting-framework programs, found in the ticket's <c>QRF</c> folder.</summary>
    Qrf
}
