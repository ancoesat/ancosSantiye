using System.Text.Json;
using AncosSantiye.Adapters;
using AncosSantiye.Events;

namespace AncosSantiye.Tests;

public class StatusLineFeedTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 24, 20, 0, 0, TimeSpan.Zero);

    // Trimmed from the status line docs.
    private const string Status = """
        {
          "cwd": "/current/working/directory",
          "session_id": "abc123",
          "transcript_path": "/path/to/transcript.jsonl",
          "model": { "id": "claude-opus-5-5", "display_name": "Opus" },
          "workspace": { "current_dir": "/current/working/directory", "project_dir": "/home/u/ancosSantiye" },
          "cost": { "total_cost_usd": 0.01234 },
          "context_window": { "total_input_tokens": 15500, "total_output_tokens": 1200, "used_percentage": 8 },
          "rate_limits": {
            "five_hour": { "used_percentage": 23.5, "resets_at": 1738425600 },
            "seven_day": { "used_percentage": 41.2, "resets_at": 1738857600 }
          }
        }
        """;

    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;

    [Fact]
    public void Map_ReadsEveryField()
    {
        var e = new StatusLineFeed().Map(Json(Status), At)!;

        Assert.Equal(EventKinds.Usage, e.Kind);
        Assert.Equal("ancosSantiye", e.Project);
        Assert.Equal("abc123", e.Session);
        Assert.Equal("Opus", e.Data!["model"]);
        Assert.Equal("8", e.Data["contextUsedPercentage"]);
        Assert.Equal("15500", e.Data["totalInputTokens"]);
        Assert.Equal("1200", e.Data["totalOutputTokens"]);
        Assert.Equal("0.01234", e.Data["costUsd"]);
        Assert.Equal("23.5", e.Data["fiveHourUsedPercentage"]);
        Assert.Equal("1738425600", e.Data["fiveHourResetsAt"]);
        Assert.Equal("41.2", e.Data["sevenDayUsedPercentage"]);
        Assert.Equal("1738857600", e.Data["sevenDayResetsAt"]);
    }

    [Fact]
    public void Map_SameValuesTwice_GivesOneEvent()
    {
        var feed = new StatusLineFeed();

        Assert.NotNull(feed.Map(Json(Status), At));
        Assert.Null(feed.Map(Json(Status), At.AddSeconds(1)));
        Assert.NotNull(feed.Map(Json(Status.Replace("\"used_percentage\": 8", "\"used_percentage\": 9")), At.AddSeconds(2)));
    }

    [Fact]
    public void Map_WithoutRateLimits_StillWorks()
    {
        var e = new StatusLineFeed().Map(Json("""{"session_id":"s","context_window":{"used_percentage":3}}"""), At)!;

        Assert.Equal("3", e.Data!["contextUsedPercentage"]);
        Assert.False(e.Data.ContainsKey("fiveHourUsedPercentage"));
    }

    [Fact]
    public void Map_NothingUseful_GivesNoEvent()
    {
        Assert.Null(new StatusLineFeed().Map(Json("""{"session_id":"s"}"""), At));
        Assert.Null(new StatusLineFeed().Map(Json("[]"), At));
    }

    [Fact]
    public void Line_ShowsLinkAndPercentages()
    {
        Assert.Equal("santiye http://127.0.0.1:5170 | ctx 8% | 5h 23.5%", StatusLineFeed.Line(Json(Status), "http://127.0.0.1:5170", serverUp: true));
        Assert.Equal("santiye off", StatusLineFeed.Line(default, "http://127.0.0.1:5170", serverUp: false));
    }
}
