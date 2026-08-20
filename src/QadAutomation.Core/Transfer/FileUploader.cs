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
///   <item>every file about to be replaced is downloaded to the ticket folder;</item>
///   <item>only then does anything get written.</item>
/// </list>
/// <para>
/// A typo in a remote path therefore costs an error message, not a half-deployed
/// ticket. The checks are cheap; the recovery they avoid is not.
/// </para>
/// <para>
/// <b>Backups are local, and the server is left alone.</b> An earlier version
/// renamed the old file to <c>&lt;name&gt;.bak-&lt;stamp&gt;</c> beside itself,
/// which was one atomic rename and needed no download - but it left a growing
/// pile of dead files in the client's program directories, one per deploy,
/// forever. Downloading into <c>&lt;ticket&gt;\_backup\</c> instead keeps the
/// server exactly as the client's own tooling expects to find it and puts the
/// old version where the operator is already working.
/// </para>
/// <para>
/// The cost is that a backup now takes a network round trip and can fail, so
/// every backup is taken <i>before</i> the first upload rather than interleaved
/// with them. If the fourth download fails, nothing has been overwritten yet.
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
            return new UploadOutcome([], string.Empty, null);
        }

        VerifyLocalFilesExist(plan);

        var report = onProgress ?? (_ => { });

        report($"Connecting to {endpoint.Username}@{endpoint.Host}:{endpoint.Port}...");

        using var session = _sessions.Connect(endpoint);

        report($"Connected. Host key {session.HostKeyFingerprint}");

        VerifyDestinationsExist(plan, session);

        var stamp = _time.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        // One Exists call per file, up front. Asking again after the upload would
        // answer the wrong question, since by then the file always exists.
        var replacing = plan.Uploads
            .Where(upload => session.Exists(upload.RemotePath))
            .ToList();

        var backups = takeBackups
            ? DownloadBackups(plan, replacing, session, stamp, report)
            : [];

        var results = new List<UploadedFile>(plan.Uploads.Count);

        foreach (var upload in plan.Uploads)
        {
            session.Upload(upload.File.LocalPath, upload.RemotePath);

            var replaced = replacing.Contains(upload);

            report($"  {(replaced ? "replaced" : "created ")} [{upload.Kind.ToString().ToUpperInvariant()}] {upload.RemotePath}");

            results.Add(new UploadedFile(
                upload,
                replaced ? UploadAction.Replaced : UploadAction.Created,
                backups.GetValueOrDefault(upload)));
        }

        return new UploadOutcome(results, session.HostKeyFingerprint, backups.Count == 0 ? null : plan.BackupFolder(stamp));
    }

    /// <summary>
    /// Copies every file that is about to be overwritten into the ticket folder.
    /// </summary>
    /// <remarks>
    /// Runs to completion before the first upload. Backing each file up
    /// immediately before overwriting it would be simpler, but it would also mean
    /// a download failing halfway through a batch after three files had already
    /// been replaced - the half-deployed state the whole class is arranged to
    /// avoid.
    /// </remarks>
    private static Dictionary<PlannedUpload, string> DownloadBackups(
        UploadPlan plan,
        IReadOnlyList<PlannedUpload> replacing,
        ISftpSession session,
        string stamp,
        Action<string> report)
    {
        var backups = new Dictionary<PlannedUpload, string>();

        if (replacing.Count == 0)
        {
            return backups;
        }

        report($"Backing up {replacing.Count} existing file(s) to {plan.BackupFolder(stamp)}");

        foreach (var upload in replacing)
        {
            var localPath = plan.BackupPathFor(upload, stamp);

            // Created here rather than in the session: where backups live is this
            // class's policy, and the session should not have opinions about the
            // local disk beyond writing the file it was handed.
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

            session.Download(upload.RemotePath, localPath);
            backups[upload] = localPath;

            report($"  saved {upload.Kind.ToString().ToUpperInvariant()}\\{upload.File.FileName}");
        }

        return backups;
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
}
