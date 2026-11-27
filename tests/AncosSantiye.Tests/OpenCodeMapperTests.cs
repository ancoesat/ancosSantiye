using System.Text.Json;
using AncosSantiye.Adapters;
using AncosSantiye.Events;
using AncosSantiye.Scene;

namespace AncosSantiye.Tests;

public sealed class OpenCodeMapperTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);

    private static SiteEvent? Map(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return OpenCodeMapper.Map(doc.RootElement, At);
    }

    [Fact]
    public void ToolStart_KeepsOnlyTheLastSegmentOfFileAndProject()
    {
        var e = Map("""{"kind":"tool.start","session":"ses_1","project":"/home/me/kbyte","tool":"edit","file":"/home/me/kbyte/src/Api.cs"}""")!;

        Assert.Equal(Sources.OpenCode, e.Source);
        Assert.Equal("kbyte", e.Project);
        Assert.Equal("opencode:ses_1", e.Actor.Id);
        Assert.Equal("opencode", e.Actor.Name);
        Assert.Equal("Editing Api.cs", e.Data!["say"]);
        Assert.DoesNotContain("/home/me", JsonSerializer.Serialize(e, EventJson.Options));
    }

    [Fact]
    public void Bash_SaysItsOwnShortDescription_Capped()
    {
        var e = Map($$"""{"kind":"tool.start","session":"s","tool":"bash","description":"{{new string('d', 300)}}\nsecond line"}""")!;

        Assert.True(e.Data!["say"].Length <= ClaudeHookMapper.MaxSay);
        Assert.DoesNotContain("second", e.Data["say"]);
    }

    [Fact]
    public void ExtraFields_AreNeverKept()
    {
        var e = Map("""{"kind":"tool.start","session":"s","tool":"bash","command":"rm -rf / --token FAKE_SECRET_MARKER","args":{"x":"secret"}}""")!;

        var all = JsonSerializer.Serialize(e, EventJson.Options);
        Assert.DoesNotContain("FAKE_SECRET_MARKER", all);
        Assert.DoesNotContain("secret", all);
        Assert.Equal("Running a command", e.Data!["say"]);
    }

    [Fact]
    public void Todos_BecomeThePlan()
    {
        var e = Map("""
            {"kind":"tool.start","session":"s","tool":"todowrite","todos":[
              {"content":"Write the mapper","status":"completed"},
              {"content":"Test it","status":"in_progress"},
              {"content":"Ship","status":"pending"},
              {"content":"Dropped","status":"cancelled"}]}
            """)!;

        var state = SiteReducer.Reduce(SiteState.Empty, e);

        Assert.Equal("Updating the plan", e.Data!["say"]);
        Assert.Equal(3, state.TasksPlanned);
        Assert.Equal(1, state.TasksDone);
    }

    [Theory]
    [InlineData("""{"kind":"session.idle","session":"s"}""")]
    [InlineData("""{"kind":"tool.start","session":"s"}""")]
    [InlineData("""{"kind":"tool.start","tool":"read"}""")]
    [InlineData("""{"kind":"tool.start","session":"bad id with spaces","tool":"read"}""")]
    [InlineData("""{"kind":"tool.start","session":"s","tool":"bad tool name"}""")]
    [InlineData("""{"kind":"usage","session":"s"}""")]
    [InlineData("[1]")]
    public void Garbage_IsIgnored(string json)
    {
        Assert.Null(Map(json));
    }

    [Theory]
    [InlineData("agent.start")]
    [InlineData("agent.stop")]
    [InlineData("turn.stop")]
    public void Lifecycle_NeedsNoTool(string kind)
    {
        Assert.Equal(kind, Map($$"""{"kind":"{{kind}}","session":"s"}""")!.Kind);
    }

    [Fact]
    public void ToolEnd_HasNoBubble()
    {
        var e = Map("""{"kind":"tool.end","session":"s","tool":"read"}""")!;

        Assert.False(e.Data!.ContainsKey("say"));
    }
}
