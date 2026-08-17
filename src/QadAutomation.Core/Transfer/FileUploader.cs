using System.Globalization;
using QadAutomation.Core.Configuration;

namespace QadAutomation.Core.Transfer;

/// <summary>
/// The upload step: connect, check, back up, write.
/// </summary>
/// <remarks>
/// <para>
/// The ordering here is the whole design. Everything that can be checked is
/// checked <i>before</i> the first byte is written, because a batch that fails
/// halfway leaves a client's server in a state nobody planned - some programs
/// new, some old, and no record of which. Concretely:
/// </para>
/// <list type="number">
///   <item>every local file must still exist;</item>
///   <item>every destination directory must exist on the server;</item>
///   <item>only then does anything get written.</item>
/// </list>
/// <para>
/// A typo in a remote path therefore costs an error message, not a half-deployed
/// ticket. The checks are cheap; the recovery they avoid is not.
/// </para>
/// </remarks>
public sealed class FileUploader : IFileUploader
{
    private readonly ISftpSessionFactory _sessions;
    private readonly TimeProvider _time;

    /// <param name="sessions">Opens the SFTP connection.</param>
    /// <param name="time">
    /// Injected so backup names are deterministic in tests. Backup paths are
    /// asserted on, and an untestable timestamp would mean asserting on a
    /// substring and hoping.
    /// </param>
    public FileUploader(ISftpSessionFactory sessions, TimeProvider? time = null)
    {
        _sessions = sessions;
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public UploadOutcome Upload(
        UploadPlan plan,
        SshEndpoint endpoint,
        bool takeBackups = true,
        Action<string>? onProgress = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(endpoint);

        if (plan.IsEmpty)
        {
            return new UploadOutcome([], string.Empty);
        }

        VerifyLocalFilesExist(plan);

        var report = onProgress ?? (_ => { });

        report($"Connecting to {endpoint.Username}@{endpoint.Host}:{endpoint.Port}...");

        using var session = _sessions.Connect(endpoint);

        report($"Connected. Host key {session.HostKeyFingerprint}");

        VerifyDestinationsExist(plan, session);

        var results = new List<UploadedFile>(plan.Uploads.Count);
        var stamp = _time.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        foreach (var upload in plan.Uploads)
        {
            results.Add(Transfer(upload, session, takeBackups, stamp, report));
        }

        return new UploadOutcome(results, session.HostKeyFingerprint);
    }

    private static UploadedFile Transfer(
        PlannedUpload upload,
        ISftpSession session,
        bool takeBackups,
        string stamp,
        Action<string> report)
    {
        var existed = session.Exists(upload.RemotePath);
        string? backupPath = null;

        if (existed && takeBackups)
        {
            // Timestamped, so a second deploy on the same day does not destroy
            // the first deploy's backup - which is precisely when you need it.
            backupPath = $"{upload.RemotePath}.bak-{stamp}";
            session.Rename(upload.RemotePath, backupPath);
            report($"  backed up {upload.File.FileName} -> {Name(backupPath)}");
        }

        session.Upload(upload.File.LocalPath, upload.RemotePath);

        report($"  {(existed ? "replaced" : "created ")} [{upload.Kind.ToString().ToUpperInvariant()}] {upload.RemotePath}");

        return new UploadedFile(
            upload,
            existed ? UploadAction.Replaced : UploadAction.Created,
            backupPath);
    }

    /// <summary>
    /// Catches a file deleted or renamed between listing the ticket and
    /// uploading it - and, more usefully, a stale plan.
    /// </summary>
    private static void VerifyLocalFilesExist(UploadPlan plan)
    {
        var missing = plan.Uploads
            .Where(u => !File.Exists(u.File.LocalPath))
            .Select(u => u.File.LocalPath)
            .ToList();

        if (missing.Count > 0)
        {
            throw new TransferException(
                $"{missing.Count} file(s) in the plan no longer exist:{Environment.NewLine}" +
                string.Join(Environment.NewLine, missing.Select(p => "  - " + p)));
        }
    }

    /// <summary>
    /// Confirms every destination directory is there before writing anything.
    /// </summary>
    /// <remarks>
    /// The directories are <b>not</b> created if absent. On a QAD server the
    /// directory layout is fixed and meaningful; a path that does not exist
    /// almost always means the config is wrong, and silently creating
    /// <c>/appl/qad/global/xrc-typo</c> would turn a loud error into a
    /// deploy that appears to succeed while putting the programs somewhere
    /// nothing will ever compile them.
    /// </remarks>
    private static void VerifyDestinationsExist(UploadPlan plan, ISftpSession session)
    {
        var missing = plan.Destinations
            .Where(directory => !session.Exists(directory))
            .ToList();

        if (missing.Count > 0)
        {
            throw new TransferException(
                $"These remote directories do not exist on the server:{Environment.NewLine}" +
                string.Join(Environment.NewLine, missing.Select(d => "  - " + d)) +
                $"{Environment.NewLine}Check srcRemotePath and qrfRemotePath for environment " +
                $"'{plan.Environment.Name}'. Nothing was uploaded.");
        }
    }

    private static string Name(string remotePath) => remotePath[(remotePath.LastIndexOf('/') + 1)..];
}
