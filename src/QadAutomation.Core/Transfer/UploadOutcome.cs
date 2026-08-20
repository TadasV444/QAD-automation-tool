namespace QadAutomation.Core.Transfer;

/// <summary>What happened to one file at its destination.</summary>
public enum UploadAction
{
    /// <summary>Nothing was there before.</summary>
    Created,

    /// <summary>A file was already there and has been overwritten.</summary>
    Replaced
}

/// <summary>One completed upload.</summary>
/// <param name="Planned">What was intended.</param>
/// <param name="Action">Whether it created or replaced.</param>
/// <param name="LocalBackupPath">
/// Where the previous version was saved on this machine, or <c>null</c> if there
/// was nothing to preserve or backups were switched off.
/// </param>
/// <remarks>
/// Recording the backup path rather than just a flag is what makes an undo
/// possible: after a bad deploy the operator has the exact paths needed to put
/// things back, printed at the time, without having to go looking. The name says
/// <c>Local</c> because it is a path on the operator's disk - nothing is left on
/// the server.
/// </remarks>
public sealed record UploadedFile(PlannedUpload Planned, UploadAction Action, string? LocalBackupPath);

/// <summary>The result of running an <see cref="UploadPlan"/>.</summary>
/// <param name="Files">One entry per file, in plan order.</param>
/// <param name="HostKeyFingerprint">The key the server presented.</param>
/// <param name="BackupFolder">
/// The folder this run's backups were written to, or <c>null</c> when none were
/// taken. Carried separately so the caller can name the folder once instead of
/// printing the common prefix on every line.
/// </param>
public sealed record UploadOutcome(
    IReadOnlyList<UploadedFile> Files,
    string HostKeyFingerprint,
    string? BackupFolder)
{
    public int CreatedCount => Files.Count(f => f.Action == UploadAction.Created);

    public int ReplacedCount => Files.Count(f => f.Action == UploadAction.Replaced);

    /// <summary>Backups taken, for the undo instructions printed afterwards.</summary>
    public IReadOnlyList<UploadedFile> WithBackups =>
        [.. Files.Where(f => f.LocalBackupPath is not null)];
}
