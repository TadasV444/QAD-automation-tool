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
/// <param name="BackupPath">
/// Where the previous version was moved to, or <c>null</c> if there was nothing
/// to preserve or backups were switched off.
/// </param>
/// <remarks>
/// Recording the backup path rather than just a flag is what makes an undo
/// possible: after a bad deploy the operator has the exact remote paths needed
/// to put things back, printed at the time, without having to go looking.
/// </remarks>
public sealed record UploadedFile(PlannedUpload Planned, UploadAction Action, string? BackupPath);

/// <summary>The result of running an <see cref="UploadPlan"/>.</summary>
public sealed record UploadOutcome(
    IReadOnlyList<UploadedFile> Files,
    string HostKeyFingerprint)
{
    public int CreatedCount => Files.Count(f => f.Action == UploadAction.Created);

    public int ReplacedCount => Files.Count(f => f.Action == UploadAction.Replaced);

    /// <summary>Backups taken, for the undo instructions printed afterwards.</summary>
    public IReadOnlyList<UploadedFile> WithBackups =>
        [.. Files.Where(f => f.BackupPath is not null)];
}
