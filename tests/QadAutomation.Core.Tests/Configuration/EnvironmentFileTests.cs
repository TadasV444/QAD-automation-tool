using QadAutomation.Core.Configuration;

namespace QadAutomation.Core.Tests.Configuration;

public sealed class EnvironmentFileTests : IDisposable
{
    private readonly string _folder;

    public EnvironmentFileTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "qad-env-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Key_value_pairs_are_read()
    {
        var variables = Load("A=1\nB=two");

        Assert.Equal("1", variables["A"]);
        Assert.Equal("two", variables["B"]);
    }

    [Fact]
    public void Blank_lines_and_comment_lines_are_ignored()
    {
        var variables = Load("# a comment\n\n   \nA=1\n# another\n");

        Assert.Single(variables);
        Assert.Equal("1", variables["A"]);
    }

    [Fact]
    public void A_hash_inside_a_value_is_kept()
    {
        // The critical one for a secrets file. Treating '#' as a trailing comment
        // would silently truncate any password containing one, and the operator
        // would be left debugging an authentication failure with a correct-looking
        // .env in front of them.
        Assert.Equal("pa#ss#word", Load("P=pa#ss#word")["P"]);
    }

    [Fact]
    public void An_equals_sign_inside_a_value_is_kept()
    {
        // Base64 secrets routinely end in '='. Only the FIRST '=' separates.
        Assert.Equal("abc==", Load("P=abc==")["P"]);
    }

    [Fact]
    public void Surrounding_quotes_are_stripped()
    {
        Assert.Equal("hunter2", Load("P=\"hunter2\"")["P"]);
        Assert.Equal("hunter2", Load("Q='hunter2'")["Q"]);
    }

    [Fact]
    public void Quotes_preserve_significant_whitespace()
    {
        Assert.Equal(" pw ", Load("P=\" pw \"")["P"]);
    }

    [Fact]
    public void An_export_prefix_is_accepted()
    {
        Assert.Equal("1", Load("export A=1")["A"]);
    }

    [Fact]
    public void Keys_are_case_sensitive()
    {
        var variables = Load("Password=a\nPASSWORD=b");

        Assert.Equal("a", variables["Password"]);
        Assert.Equal("b", variables["PASSWORD"]);
    }

    [Fact]
    public void A_line_that_is_not_a_pair_is_an_error_naming_the_line_number()
    {
        var message = Assert.Throws<ConfigurationException>(() => Load("A=1\nthis is not a pair")).Message;

        Assert.Contains("line 2", message);
    }

    [Fact]
    public void A_missing_file_yields_no_variables_rather_than_an_error()
    {
        // A config using no ${...} placeholders needs no .env at all. An actually
        // missing variable is reported later, by name, by VariableExpander.
        Assert.Empty(EnvironmentFile.Load(Path.Combine(_folder, "absent")));
    }

    [Fact]
    public void Process_environment_variables_override_the_file()
    {
        var name = "QAD_TEST_" + Guid.NewGuid().ToString("N");
        var path = WriteEnv($"{name}=from-file");

        Environment.SetEnvironmentVariable(name, "from-process");
        try
        {
            Assert.Equal("from-process", EnvironmentFile.LoadWithProcessEnvironment(path)[name]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    private string WriteEnv(string content)
    {
        var path = Path.Combine(_folder, ".env");
        File.WriteAllText(path, content);
        return path;
    }

    private IReadOnlyDictionary<string, string> Load(string content) => EnvironmentFile.Load(WriteEnv(content));
}
