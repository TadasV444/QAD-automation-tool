using QadAutomation.Core;
using QadAutomation.Core.Configuration;
using QadAutomation.Core.Tickets;
using QadAutomation.Core.Transfer;
using Microsoft.Extensions.Time.Testing;

namespace QadAutomation.Core.Tests.Transfer;

public sealed class FileUploaderTests : IDisposable
{
    private const string SrcPath = "/appl/global/xrc";
    private const string QrfPath = "/appl/desktop/test/reports";

    private readonly string _local;
    private readonly FakeSftpServer _server = new FakeSftpServer().WithDirectory(SrcPath, QrfPath);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 8, 17, 9, 30, 0, TimeSpan.Zero));

    public FileUploaderTests()
    {
        _local = Path.Combine(Path.GetTempPath(), "qad-up-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_local);
    }

    public void Dispose()
    {
        if (Directory.Exists(_local))
        {
            Directory.Delete(_local, recursive: true);
        }
    }

    [Fact]
    public void A_new_file_is_created_on_the_server()
    {
        var plan = Plan(Local("a.p", "PROGRAM A", ProgramKind.Src));

        var outcome = Upload(plan);

        Assert.Equal("PROGRAM A", _server.Files[$"{SrcPath}/a.p"]);
        Assert.Equal(1, outcome.CreatedCount);
        Assert.Equal(0, outcome.ReplacedCount);
    }

    [Fact]
    public void An_existing_file_is_backed_up_before_being_replaced()
    {
        // The property that matters: the backup holds the OLD content. A mock
        // asserting "Rename was called" would pass even if the order were wrong.
        _server.WithFile($"{SrcPath}/a.p", "OLD VERSION");

        var outcome = Upload(Plan(Local("a.p", "NEW VERSION", ProgramKind.Src)));

        Assert.Equal("NEW VERSION", _server.Files[$"{SrcPath}/a.p"]);
        Assert.Equal("OLD VERSION", _server.Files[$"{SrcPath}/a.p.bak-20260817-093000"]);
        Assert.Equal(1, outcome.ReplacedCount);
    }

    [Fact]
    public void The_backup_path_is_reported_so_the_operator_can_undo()
    {
        _server.WithFile($"{SrcPath}/a.p", "OLD");

        var outcome = Upload(Plan(Local("a.p", "NEW", ProgramKind.Src)));

        Assert.Equal($"{SrcPath}/a.p.bak-20260817-093000", Assert.Single(outcome.WithBackups).BackupPath);
    }

    [Fact]
    public void Backups_are_timestamped_so_a_second_deploy_does_not_destroy_the_first()
    {
        _server.WithFile($"{SrcPath}/a.p", "V1");
        Upload(Plan(Local("a.p", "V2", ProgramKind.Src)));

        _time.Advance(TimeSpan.FromHours(2));
        Upload(Plan(Local("a.p", "V3", ProgramKind.Src)));

        Assert.Equal("V1", _server.Files[$"{SrcPath}/a.p.bak-20260817-093000"]);
        Assert.Equal("V2", _server.Files[$"{SrcPath}/a.p.bak-20260817-113000"]);
        Assert.Equal("V3", _server.Files[$"{SrcPath}/a.p"]);
    }

    [Fact]
    public void Backups_can_be_switched_off()
    {
        _server.WithFile($"{SrcPath}/a.p", "OLD");

        var outcome = Upload(Plan(Local("a.p", "NEW", ProgramKind.Src)), takeBackups: false);

        Assert.Empty(outcome.WithBackups);
        Assert.DoesNotContain(_server.Files.Keys, k => k.Contains(".bak", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_remote_directory_stops_the_run_before_anything_is_written()
    {
        // The important guarantee: no half-deployed ticket. A typo in a remote
        // path must cost an error message, not three of five files uploaded.
        var server = new FakeSftpServer().WithDirectory(SrcPath); // no QRF directory

        var plan = Plan(
            Local("a.p", "A", ProgramKind.Src),
            Local("b.p", "B", ProgramKind.Qrf));

        var message = Assert.Throws<TransferException>(
            () => new FileUploader(server, _time).Upload(plan, Endpoint)).Message;

        Assert.Contains(QrfPath, message);
        Assert.Contains("Nothing was uploaded", message);
        Assert.Empty(server.Files);
    }

    [Fact]
    public void A_local_file_that_vanished_stops_the_run_before_connecting()
    {
        var file = Local("a.p", "A", ProgramKind.Src);
        File.Delete(file.LocalPath);

        Assert.Throws<TransferException>(() => Upload(Plan(file)));

        Assert.Null(_server.ConnectedTo);
    }

    [Fact]
    public void Each_kind_lands_in_its_own_directory()
    {
        Upload(Plan(
            Local("a.p", "A", ProgramKind.Src),
            Local("b.p", "B", ProgramKind.Qrf)));

        Assert.Equal("A", _server.Files[$"{SrcPath}/a.p"]);
        Assert.Equal("B", _server.Files[$"{QrfPath}/b.p"]);
    }

    [Fact]
    public void An_empty_plan_does_not_even_connect()
    {
        var outcome = new FileUploader(_server, _time).Upload(
            UploadPlan.Create(new TicketFolder("T", "p", []), Environment(), "pilot"),
            Endpoint);

        Assert.Empty(outcome.Files);
        Assert.Null(_server.ConnectedTo);
    }

    [Fact]
    public void The_session_is_always_disposed()
    {
        Upload(Plan(Local("a.p", "A", ProgramKind.Src)));

        Assert.True(_server.IsDisposed);
    }

    [Fact]
    public void A_refused_login_surfaces_as_a_transfer_error()
    {
        _server.ConnectFailure = new TransferException("'mfg' was refused by qad.example.");

        Assert.Contains("refused", Assert.Throws<TransferException>(
            () => Upload(Plan(Local("a.p", "A", ProgramKind.Src)))).Message);
    }

    [Fact]
    public void Progress_is_reported_per_file_without_revealing_the_password()
    {
        var lines = new List<string>();

        new FileUploader(_server, _time).Upload(
            Plan(Local("a.p", "A", ProgramKind.Src)),
            Endpoint,
            onProgress: lines.Add);

        Assert.Contains(lines, l => l.Contains($"{SrcPath}/a.p", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("hunter2", StringComparison.Ordinal));
    }

    // --- helpers ---------------------------------------------------------

    private static readonly SshEndpoint Endpoint = new("qad.example", 22, "mfg", "hunter2", null);

    private ProgramFile Local(string name, string contents, ProgramKind kind)
    {
        var path = Path.Combine(_local, name);
        File.WriteAllText(path, contents);
        return new ProgramFile(kind, path);
    }

    private UploadOutcome Upload(UploadPlan plan, bool takeBackups = true) =>
        new FileUploader(_server, _time).Upload(plan, Endpoint, takeBackups);

    private static UploadPlan Plan(params ProgramFile[] files) =>
        UploadPlan.Create(new TicketFolder("Ticket 9999555", @"C:\t", files), Environment(), "pilot");

    private static QadEnvironment Environment() =>
        new(
            "TEST",
            false,
            Endpoint,
            new RemotePaths(SrcPath, QrfPath),
            new CompileSettings(CompileStrategy.InteractiveMenu, []));
}
