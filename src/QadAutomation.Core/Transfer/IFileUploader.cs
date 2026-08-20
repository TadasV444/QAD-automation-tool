using QadAutomation.Core.Configuration;

namespace QadAutomation.Core.Transfer;

/// <summary>
/// Executes an <see cref="UploadPlan"/> against a server.
/// </summary>
public interface IFileUploader
{
    /// <summary>
    /// Uploads every file in <paramref name="plan"/>.
    /// </summary>
    /// <param name="plan">Already-resolved sources and destinations.</param>
    /// <param name="endpoint">Where to connect.</param>
    /// <param name="takeBackups">
    /// Download each file that is about to be overwritten into the ticket
    /// folder first. Nothing is written to the server either way.
    /// </param>
    /// <param name="onProgress">
    /// Called with a human-readable line per step. A callback rather than a
    /// <c>TextWriter</c> so Core still knows nothing about consoles - the CLI
    /// decides whether these become output, a log, or nothing at all.
    /// </param>
    /// <exception cref="TransferException">If the transfer cannot be completed.</exception>
    UploadOutcome Upload(
        UploadPlan plan,
        SshEndpoint endpoint,
        bool takeBackups = true,
        Action<string>? onProgress = null);
}
