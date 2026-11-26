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
        Assert.Equal("chief:abc123", e.Actor.Id);
        Assert.Equal(At, e.Ts);
    }

    [Fact]
    public void TwoSessions_AreTwoWorkers()
    {
        var first = Map(PreToolUse)!;
        var second = Map(PreToolUse.Replace("abc123", "def456"))!;
        var noSession = Map(PreToolUse.Replace("\"session_id\": \"abc123\",", ""))!;

        Assert.NotEqual(first.Actor.Id, second.Actor.Id);
        Assert.Equal(Roles.Chief, second.Actor.Role);
        Assert.Equal("chief", noSession.Actor.Id);
    }

    [Fact]
    public void TheCommandNeverReachesTheEvent_OnlyTheDescription()
    {
        var json = JsonSerializer.Serialize(Map(PreToolUse), EventJson.Options);

        Assert.DoesNotContain("SECRET_TOKEN_123", json);
        Assert.DoesNotContain("npm test", json);
        Assert.DoesNotContain("transcript", json);
        Assert.Equal("Run test suite", Map(PreToolUse)!.Data!["say"]);
    }

    private static string Say(string tool, string input) =>
        ClaudeHookMapper.Say(tool, JsonDocument.Parse(input).RootElement);

    [Theory]
    [InlineData("Read", """{ "file_path": "C:\\Users\\esat\\secret-client\\src\\Program.cs" }""", "Reading Program.cs")]
    [InlineData("Write", """{ "file_path": "/home/u/private/notes.md", "content": "TOP SECRET" }""", "Writing notes.md")]
    [InlineData("Edit", """{ "file_path": "/a/b/page.tsx", "old_string": "x", "new_string": "y" }""", "Editing page.tsx")]
    [InlineData("MultiEdit", """{ "file_path": "/a/b/site.css" }""", "Editing site.css")]
    [InlineData("NotebookEdit", """{ "notebook_path": "/a/analysis.ipynb" }""", "Editing analysis.ipynb")]
    [InlineData("Read", """{}""", "Reading a file")]
    [InlineData("Grep", """{ "pattern": "password=.*" }""", "Searching the code")]
    [InlineData("Glob", """{ "pattern": "**/*.env" }""", "Looking for files")]
    [InlineData("Bash", """{ "command": "rm -rf /tmp/x" }""", "Running a command")]
    [InlineData("Bash", """{ "command": "x", "description": "Install deps\nand more" }""", "Install deps")]
    [InlineData("WebFetch", """{ "url": "https://docs.example.com/a?token=abc" }""", "Reading docs.example.com")]
    [InlineData("WebFetch", """{ "url": "not a url" }""", "Reading a page")]
    [InlineData("WebSearch", """{ "query": "my secret plan" }""", "Searching the web")]
    [InlineData("Task", """{ "subagent_type": "Explore", "prompt": "secret brief" }""", "Calling Explore")]
    [InlineData("Agent", """{ "prompt": "secret brief" }""", "Calling a helper")]
    [InlineData("TodoWrite", """{ "todos": [ { "content": "secret todo" } ] }""", "Updating the task list")]
    [InlineData("AskUserQuestion", """{}""", "Asking you something")]
    [InlineData("Skill", """{ "skill": "xlsx" }""", "Using skill xlsx")]
    [InlineData("mcp__github__create_issue", """{ "title": "secret" }""", "Using github")]
    [InlineData("SomethingNew", """{}""", "Using SomethingNew")]
    public void Say_ReadsOnlySafeParts(string tool, string input, string expected)
    {
        Assert.Equal(expected, Say(tool, input));
    }

    [Fact]
    public void Say_IsShort()
    {
        var line = Say("Bash", $$"""{ "description": "{{new string('x', 300)}}" }""");

        Assert.Equal(ClaudeHookMapper.MaxSay, line.Length);
        Assert.EndsWith("...", line);
    }

    [Fact]
    public void Say_IsOnlyOnStartAndFail()
    {
        var end = Map(PreToolUse.Replace("PreToolUse", "PostToolUse"))!;
        var fail = Map(PreToolUse.Replace("PreToolUse", "PostToolUseFailure"))!;

        Assert.False(end.Data!.ContainsKey("say"));
        Assert.Equal("Run test suite", fail.Data!["say"]);
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
