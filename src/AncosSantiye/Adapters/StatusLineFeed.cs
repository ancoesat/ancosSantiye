using System.Text.Json;
using AncosSantiye.Events;

namespace AncosSantiye.Adapters;

/// <summary>
/// Reads the JSON Claude Code gives a status line command: context use, tokens, cost and,
/// for Pro and Max, the 5 hour and 7 day limit use. Becomes a <c>usage</c> event.
/// </summary>
public sealed class StatusLineFeed
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Dictionary<string, string>> _last = [];

    /// <summary>Field name in the event, then the path in the status line JSON.</summary>
    private static readonly (string Key, string[] Path)[] Fields =
    [
        ("model", ["model", "display_name"]),
        ("contextUsedPercentage", ["context_window", "used_percentage"]),
        ("totalInputTokens", ["context_window", "total_input_tokens"]),
        ("totalOutputTokens", ["context_window", "total_output_tokens"]),
        ("costUsd", ["cost", "total_cost_usd"]),
        ("fiveHourUsedPercentage", ["rate_limits", "five_hour", "used_percentage"]),
        ("fiveHourResetsAt", ["rate_limits", "five_hour", "resets_at"]),
        ("sevenDayUsedPercentage", ["rate_limits", "seven_day", "used_percentage"]),
        ("sevenDayResetsAt", ["rate_limits", "seven_day", "resets_at"]),
    ];

    public static Dictionary<string, string> Read(JsonElement status)
    {
        var data = new Dictionary<string, string>();
        foreach (var (key, path) in Fields)
        {
            if (Find(status, path) is { } value)
            {
                data[key] = value;
            }
        }

        return data;
    }

    /// <summary>The status line runs very often. Returns an event only when something changed.</summary>
    public SiteEvent? Map(JsonElement status, DateTimeOffset at)
    {
        if (status.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var data = Read(status);
        if (data.Count == 0)
        {
            return null;
        }

        var session = Find(status, ["session_id"]);
        lock (_gate)
        {
            var key = session ?? "";
            if (_last.TryGetValue(key, out var previous) && previous.Count == data.Count && !previous.Except(data).Any())
            {
                return null;
            }

            if (_last.Count >= 100 && !_last.ContainsKey(key))
            {
                _last.Clear();
            }

            _last[key] = data;
        }

        var dir = Find(status, ["workspace", "project_dir"]) ?? Find(status, ["cwd"]);
        return SiteEvent.Create(
            Sources.ClaudeCode,
            ClaudeHookMapper.ProjectName(dir),
            ClaudeHookMapper.Chief,
            EventKinds.Usage,
            session: session,
            data: data,
            ts: at);
    }

    /// <summary>The short line printed in the terminal, built from the same JSON.</summary>
    public static string Line(JsonElement status, string url, bool serverUp)
    {
        var parts = new List<string> { serverUp ? $"santiye {url}" : "santiye off" };
        if (status.ValueKind == JsonValueKind.Object)
        {
            var data = Read(status);
            if (data.TryGetValue("contextUsedPercentage", out var ctx))
            {
                parts.Add($"ctx {ctx}%");
            }

            if (data.TryGetValue("fiveHourUsedPercentage", out var fiveHour))
            {
                parts.Add($"5h {fiveHour}%");
            }
        }

        return string.Join(" | ", parts);
    }

    private static string? Find(JsonElement e, string[] path)
    {
        foreach (var name in path)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out e))
            {
                return null;
            }
        }

        return e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.GetRawText(),
            _ => null,
        };
    }
}
