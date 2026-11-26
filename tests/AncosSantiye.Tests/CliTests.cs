using System.Text.Json.Nodes;

namespace AncosSantiye.Tests;

public class CliTests
{
    [Fact]
    public async Task HooksPrint_PrintsValidJsonWithOurHookUrl()
    {
        var output = new StringWriter();

        var code = await Cli.Run(["hooks", "print"], new StringReader(""), output, new StringWriter());

        Assert.Equal(0, code);
        Assert.Contains("http://127.0.0.1:5170/hooks/claude", output.ToString());
        Assert.NotNull(JsonNode.Parse(output.ToString()));
    }

    [Fact]
    public async Task StatusLine_ServerDown_StillPrintsALine()
    {
        var output = new StringWriter();

        var code = await Cli.Run(["statusline"], new StringReader("""{"context_window":{"used_percentage":5}}"""), output, new StringWriter());

        Assert.Equal(0, code);
        Assert.StartsWith("santiye", output.ToString());
        Assert.Contains("ctx 5%", output.ToString());
    }

    [Fact]
    public async Task AgyHook_ServerDown_StillAnswersWithEmptyJson()
    {
        var output = new StringWriter();

        var code = await Cli.Run(["hook", "--from", "agy", "--event", "PreToolUse"], new StringReader("{}"), output, new StringWriter());

        Assert.Equal(0, code);
        Assert.Equal("{}", output.ToString().Trim());
    }

    [Theory]
    [InlineData("hook", "--from", "agy", "--event", "Bogus")]
    [InlineData("hook", "--from", "codex", "--event", "Stop")]
    [InlineData("connect", "codex")]
    [InlineData("disconnect", "everything")]
    [InlineData("connect")]
    public async Task WrongShapes_NeverTouchAnything(params string[] args)
    {
        Assert.Equal(2, await Cli.Run(args, new StringReader(""), new StringWriter(), new StringWriter()));
    }

    [Fact]
    public async Task UnknownCommand_ShowsUsage()
    {
        var error = new StringWriter();

        Assert.Equal(2, await Cli.Run(["nope"], new StringReader(""), new StringWriter(), error));
        Assert.Contains("Usage", error.ToString());
    }

    [Theory]
    [InlineData("hooks", "install", "--settings")]
    [InlineData("hooks", "install", "--setting", "x.json")]
    [InlineData("hooks", "install", "x.json")]
    [InlineData("hooks", "uninstall", "--settings")]
    public async Task HooksWithBadArguments_NeverFallBackToTheRealSettings(params string[] args)
    {
        var error = new StringWriter();

        Assert.Equal(2, await Cli.Run(args, new StringReader(""), new StringWriter(), error));
        Assert.Contains("Usage", error.ToString());
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public async Task Help_PrintsUsageAndIsNotAServerStart(string flag)
    {
        var output = new StringWriter();

        Assert.False(Cli.IsServerStart([flag]));
        Assert.Equal(0, await Cli.Run([flag], new StringReader(""), output, new StringWriter()));
        Assert.Contains("Usage", output.ToString());
    }

    [Theory]
    [InlineData(new string[0], true)]
    [InlineData(new[] { "--urls", "http://127.0.0.1:6000" }, true)]
    [InlineData(new[] { "hooks", "print" }, false)]
    public void IsServerStart_OnlyForNoArgsOrOptions(string[] args, bool expected)
    {
        Assert.Equal(expected, Cli.IsServerStart(args));
    }

    [Fact]
    public async Task Install_SaysLoudlyWhenAnExistingStatusLineWasKept()
    {
        var dir = Path.Combine(Path.GetTempPath(), "santiye-cli-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var settings = Path.Combine(dir, "settings.json");
            File.WriteAllText(settings, """{ "statusLine": { "type": "command", "command": "my-line.sh" } }""");
            var output = new StringWriter();

            var code = await Cli.Run(["hooks", "install", "--settings", settings], new StringReader(""), output, new StringWriter());

            Assert.Equal(0, code);
            Assert.Contains("NOTE: you already have a status line", output.ToString());
            Assert.Contains("Budget panel stays empty", output.ToString());
            Assert.Contains("| ancos-santiye statusline", output.ToString());
            Assert.Contains("my-line.sh", File.ReadAllText(settings));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Hook_IsSilentAndExitsZero_EvenWithNoServer()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var code = await Cli.Run(["hook"], new StringReader("""{"hook_event_name":"SessionStart"}"""), output, error);

        Assert.Equal(0, code);
        Assert.Equal("", output.ToString() + error.ToString());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
        Assert.False(Cli.IsServerStart(["hook"]));
    }

    [Fact]
    public async Task Doctor_AlwaysAnswers_WithOrWithoutAServer()
    {
        var output = new StringWriter();

        var code = await Cli.Run(["doctor"], new StringReader(""), output, new StringWriter());

        Assert.Equal(0, code);
        Assert.Contains("ancosSantiye server:", output.ToString());
        Assert.False(Cli.IsServerStart(["doctor"]));
    }
}
