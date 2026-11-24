using System.Text.Json;
using AncosSantiye.Adapters;
using AncosSantiye.Events;

namespace AncosSantiye.Tests;

public class ClaudeHookMapperTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 24, 20, 0, 0, TimeSpan.Zero);

    private static SiteEvent? Map(string json) => ClaudeHookMapper.Map(JsonDocument.Parse(json).RootElement, At);

    // Shape from the Claude Code hooks docs, with a secret-looking tool input.
    private const string PreToolUse = """
        {
          "session_id": "abc123",
          "transcript_path": "/home/user/.claude/projects/x/transcript.jsonl",
          "cwd": "/home/user/my-project",
          "hook_event_name": "PreToolUse",
          "tool_name": "Bash",
          "tool_input": { "command": "echo SECRET_TOKEN_123 && npm test", "description": "Run test suite" },
          "tool_use_id": "toolu_01ABC123"
        }
        """;

    [Fact]
    public void PreToolUse_BecomesToolStartForTheChief()
    {
        var e = Map(PreToolUse)!;

        Assert.Equal(EventKinds.ToolStart, e.Kind);
        Assert.Equal("Bash", e.Data!["tool"]);
        Assert.Equal("my-project", e.Project);
        Assert.Equal("abc123", e.Session);
        Assert.Equal("chief", e.Actor.Id);
        Assert.Equal(At, e.Ts);
    }

    [Fact]
    public void ToolInput_NeverReachesTheEvent()
    {
        var json = JsonSerializer.Serialize(Map(PreToolUse), EventJson.Options);

        Assert.DoesNotContain("SECRET_TOKEN_123", json);
        Assert.DoesNotContain("Run test suite", json);
        Assert.DoesNotContain("transcript", json);
    }

    [Theory]
    [InlineData("SessionStart", EventKinds.SessionStart)]
    [InlineData("SessionEnd", EventKinds.SessionEnd)]
    [InlineData("PreToolUse", EventKinds.ToolStart)]
    [InlineData("PostToolUse", EventKinds.ToolEnd)]
    [InlineData("PostToolUseFailure", EventKinds.ToolFail)]
    [InlineData("SubagentStart", EventKinds.AgentStart)]
    [InlineData("SubagentStop", EventKinds.AgentStop)]
    [InlineData("Stop", EventKinds.TurnStop)]
    [InlineData("TaskCreated", EventKinds.TaskCreated)]
    [InlineData("TaskCompleted", EventKinds.TaskCompleted)]
    public void EachHook_MapsToItsKind(string hook, string kind)
    {
        Assert.Equal(kind, Map($$"""{"hook_event_name":"{{hook}}","cwd":"/p/demo"}""")!.Kind);
    }

    [Theory]
    [InlineData("UserPromptSubmit")]
    [InlineData("Notification")]
    [InlineData("")]
    public void OtherHooks_AreIgnored(string hook)
    {
        Assert.Null(Map($$"""{"hook_event_name":"{{hook}}","prompt":"hello"}"""));
    }

    [Fact]
    public void NotAnObject_IsIgnored()
    {
        Assert.Null(Map("[1,2]"));
    }

    [Theory]
    [InlineData("Explore", Roles.Worker)]
    [InlineData("denetci", Roles.Inspector)]
    [InlineData("danisman", Roles.Architect)]
    public void HookInsideASubagent_UsesTheAgentAsActor(string agentType, string role)
    {
        var e = Map($$"""{"hook_event_name":"PreToolUse","tool_name":"Grep","cwd":"/p/demo","agent_id":"agent_7f2","agent_type":"{{agentType}}"}""")!;

        Assert.Equal("agent_7f2", e.Actor.Id);
        Assert.Equal(role, e.Actor.Role);
        Assert.Equal(agentType, e.Actor.Name);
    }

    [Theory]
    [InlineData(@"C:\Users\ancoesat\Developer\GitHubRepos\ancosSantiye", "ancosSantiye")]
    [InlineData(@"C:\Users\ancoesat\Developer\GitHubRepos\ancosSantiye\", "ancosSantiye")]
    [InlineData("/Users/esat/dev/kasa-api/", "kasa-api")]
    [InlineData(null, "unknown")]
    [InlineData("", "unknown")]
    public void ProjectName_WorksForWindowsAndPosixPaths(string? cwd, string expected)
    {
        Assert.Equal(expected, ClaudeHookMapper.ProjectName(cwd));
    }
}
