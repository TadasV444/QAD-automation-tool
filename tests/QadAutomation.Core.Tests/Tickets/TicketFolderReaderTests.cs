using QadAutomation.Core;
using QadAutomation.Core.Tickets;

namespace QadAutomation.Core.Tests.Tickets;

/// <summary>
/// Tests for SRC/QRF discovery, run against a real temporary folder tree.
/// </summary>
/// <remarks>
/// These deliberately use the real filesystem rather than an <c>IFileSystem</c>
/// mock. The behaviour that matters here - case-insensitive folder matching,
/// hidden-file handling, ordering - is behaviour of the filesystem itself, and a
/// mock would let us assert whatever we felt like instead of what Windows does.
/// </remarks>
public sealed class TicketFolderReaderTests : IDisposable
{
    private readonly string _workingFolder;

    public TicketFolderReaderTests()
    {
        _workingFolder = Path.Combine(Path.GetTempPath(), "qad-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workingFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingFolder))
        {
            Directory.Delete(_workingFolder, recursive: true);
        }
    }

    [Fact]
    public void Files_are_classified_by_the_folder_they_are_in()
    {
        GivenTicket("Ticket 9999555", src: ["xxmtpo.p"], qrf: ["xxrpt01.p"]);

        var folder = Read("Ticket 9999555");

        Assert.Equal("xxmtpo.p", Assert.Single(folder.OfKind(ProgramKind.Src)).FileName);
        Assert.Equal("xxrpt01.p", Assert.Single(folder.OfKind(ProgramKind.Qrf)).FileName);
    }

    [Fact]
    public void A_ticket_with_only_src_yields_only_src()
    {
        GivenTicket("Ticket 100", src: ["a.p", "b.p"]);

        var folder = Read("Ticket 100");

        Assert.Equal([ProgramKind.Src], folder.KindsPresent);
        Assert.Equal(2, folder.Files.Count);
    }

    [Fact]
    public void A_ticket_with_only_qrf_yields_only_qrf()
    {
        GivenTicket("Ticket 200", qrf: ["r.p"]);

        Assert.Equal([ProgramKind.Qrf], Read("Ticket 200").KindsPresent);
    }

    [Fact]
    public void A_ticket_with_neither_folder_is_empty_rather_than_an_error()
    {
        GivenTicket("Ticket 300");

        var folder = Read("Ticket 300");

        Assert.True(folder.IsEmpty);
        Assert.Empty(folder.KindsPresent);
    }

    [Theory]
    [InlineData("src")]
    [InlineData("Src")]
    [InlineData("SRC")]
    public void Kind_folder_names_are_matched_case_insensitively(string folderName)
    {
        var ticket = Directory.CreateDirectory(Path.Combine(_workingFolder, "Ticket 400"));
        var kindFolder = Directory.CreateDirectory(Path.Combine(ticket.FullName, folderName));
        File.WriteAllText(Path.Combine(kindFolder.FullName, "a.p"), string.Empty);

        Assert.Equal(ProgramKind.Src, Assert.Single(Read("Ticket 400").Files).Kind);
    }

    [Fact]
    public void Unknown_sub_folders_are_ignored_not_deployed()
    {
        var ticket = Directory.CreateDirectory(Path.Combine(_workingFolder, "Ticket 500"));
        var other = Directory.CreateDirectory(Path.Combine(ticket.FullName, "Docs"));
        File.WriteAllText(Path.Combine(other.FullName, "notes.txt"), string.Empty);

        Assert.True(Read("Ticket 500").IsEmpty);
    }

    [Fact]
    public void Nested_folders_inside_src_are_not_recursed_into()
    {
        var ticket = Directory.CreateDirectory(Path.Combine(_workingFolder, "Ticket 600"));
        var src = Directory.CreateDirectory(Path.Combine(ticket.FullName, "SRC"));
        File.WriteAllText(Path.Combine(src.FullName, "top.p"), string.Empty);

        var nested = Directory.CreateDirectory(Path.Combine(src.FullName, "old"));
        File.WriteAllText(Path.Combine(nested.FullName, "buried.p"), string.Empty);

        Assert.Equal("top.p", Assert.Single(Read("Ticket 600").Files).FileName);
    }

    [Fact]
    public void Hidden_and_temporary_files_are_skipped()
    {
        var ticket = Directory.CreateDirectory(Path.Combine(_workingFolder, "Ticket 700"));
        var src = Directory.CreateDirectory(Path.Combine(ticket.FullName, "SRC"));

        File.WriteAllText(Path.Combine(src.FullName, "real.p"), string.Empty);
        File.WriteAllText(Path.Combine(src.FullName, "~$real.p"), string.Empty);
        File.WriteAllText(Path.Combine(src.FullName, ".gitkeep"), string.Empty);

        var hidden = Path.Combine(src.FullName, "hidden.p");
        File.WriteAllText(hidden, string.Empty);
        File.SetAttributes(hidden, FileAttributes.Hidden);

        Assert.Equal("real.p", Assert.Single(Read("Ticket 700").Files).FileName);
    }

    [Fact]
    public void A_ticket_can_be_found_by_number_alone()
    {
        GivenTicket("Ticket 9999555", src: ["a.p"]);

        Assert.Equal("Ticket 9999555", Read("9999555").Name);
    }

    [Fact]
    public void An_ambiguous_fragment_is_an_error_rather_than_a_guess()
    {
        GivenTicket("Ticket 111", src: ["a.p"]);
        GivenTicket("Ticket 1112", src: ["b.p"]);

        var message = Assert.Throws<TicketFolderException>(() => Read("111")).Message;

        Assert.Contains("matches 2 ticket folders", message);
    }

    [Fact]
    public void An_exact_name_match_wins_over_a_partial_one()
    {
        GivenTicket("Ticket 111", src: ["a.p"]);
        GivenTicket("Ticket 1112", src: ["b.p"]);

        Assert.Equal("Ticket 111", Read("Ticket 111").Name);
    }

    [Fact]
    public void An_unknown_ticket_is_an_error()
    {
        Assert.Throws<TicketFolderException>(() => Read("nope"));
    }

    [Fact]
    public void A_missing_working_folder_is_reported_clearly()
    {
        var reader = new TicketFolderReader(Path.Combine(_workingFolder, "does-not-exist"));

        Assert.Contains("does not exist", Assert.Throws<TicketFolderException>(() => reader.ListTickets()).Message);
    }

    [Fact]
    public void Tickets_are_listed_in_a_stable_order()
    {
        GivenTicket("Ticket 2");
        GivenTicket("Ticket 1");

        Assert.Equal(["Ticket 1", "Ticket 2"], new TicketFolderReader(_workingFolder).ListTickets());
    }

    // --- helpers ---------------------------------------------------------

    private void GivenTicket(string name, string[]? src = null, string[]? qrf = null)
    {
        var ticket = Directory.CreateDirectory(Path.Combine(_workingFolder, name));

        Populate(ticket.FullName, "SRC", src);
        Populate(ticket.FullName, "QRF", qrf);
    }

    private static void Populate(string ticketPath, string folderName, string[]? files)
    {
        if (files is null)
        {
            return;
        }

        var folder = Directory.CreateDirectory(Path.Combine(ticketPath, folderName));
        foreach (var file in files)
        {
            File.WriteAllText(Path.Combine(folder.FullName, file), string.Empty);
        }
    }

    private TicketFolder Read(string ticket) => new TicketFolderReader(_workingFolder).Read(ticket);
}
