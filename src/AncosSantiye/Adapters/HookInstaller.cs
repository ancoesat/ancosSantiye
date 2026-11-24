using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AncosSantiye.Adapters;

/// <summary>
/// Adds and removes our entries in a Claude Code settings.json. It never touches anything else:
/// other hooks, permissions and an existing status line stay as they are. Every write is preceded
/// by a timestamped backup, and a file it cannot fully understand is never written.
/// </summary>
public static class HookInstaller
{
    public const string StatusLineCommand = "ancos-santiye statusline";

    /// <summary>Seconds. An HTTP hook blocks Claude Code until it answers, so keep it short.</summary>
    public const int HookTimeoutSeconds = 2;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        // Keep Turkish letters and characters like & < > + as they are in the user's file.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string DefaultSettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    public static string HookUrl(string baseUrl) => baseUrl.TrimEnd('/') + "/hooks/claude";

    public sealed record Result(bool Changed, string? BackupPath, string? Error, bool StatusLineKept = false);

    /// <summary>What install adds, as JSON. Also what <c>hooks print</c> shows.</summary>
    public static JsonObject Snippet(string baseUrl)
    {
        var hooks = new JsonObject();
        foreach (var name in ClaudeHookMapper.Kinds.Keys)
        {
            hooks[name] = new JsonArray(Group(name, baseUrl));
        }

        return new JsonObject
        {
            ["hooks"] = hooks,
            ["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = StatusLineCommand },
        };
    }

    public static string Print(JsonNode node) => node.ToJsonString(WriteOptions);

    public static Result Install(string settingsPath, string baseUrl, DateTimeOffset now)
    {
        var (root, error) = Load(settingsPath);
        if (root is null)
        {
            return new Result(false, null, error);
        }

        if (Shape(root) is { } shapeError)
        {
            return new Result(false, null, $"{settingsPath}: {shapeError} Nothing was changed.");
        }

        var url = HookUrl(baseUrl);
        var changed = false;
        if (root["hooks"] is not JsonObject hooks)
        {
            hooks = [];
            root["hooks"] = hooks;
        }

        foreach (var name in ClaudeHookMapper.Kinds.Keys)
        {
            if (hooks[name] is not JsonArray groups)
            {
                groups = [];
                hooks[name] = groups;
            }

            if (!groups.Any(g => Ours(g, url).Any()))
            {
                groups.Add(Group(name, baseUrl));
                changed = true;
            }
        }

        var statusLineKept = false;
        if (root["statusLine"] is null)
        {
            root["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = StatusLineCommand };
            changed = true;
        }
        else if (!IsOurStatusLine(root["statusLine"]))
        {
            statusLineKept = true;
        }

        return changed
            ? Save(settingsPath, root, now) with { StatusLineKept = statusLineKept }
            : new Result(false, null, null, statusLineKept);
    }

    public static Result Uninstall(string settingsPath, string baseUrl, DateTimeOffset now)
    {
        if (!File.Exists(settingsPath))
        {
            return new Result(false, null, null);
        }

        var (root, error) = Load(settingsPath);
        if (root is null)
        {
            return new Result(false, null, error);
        }

        if (Shape(root) is { } shapeError)
        {
            return new Result(false, null, $"{settingsPath}: {shapeError} Nothing was changed.");
        }

        var url = HookUrl(baseUrl);
        var changed = false;
        if (root["hooks"] is JsonObject hooks)
        {
            foreach (var (name, node) in hooks.ToList())
            {
                if (node is not JsonArray groups)
                {
                    continue;
                }

                var removedGroup = false;
                foreach (var group in groups.ToList())
                {
                    var ours = Ours(group, url).ToList();
                    if (ours.Count == 0 || group?["hooks"] is not JsonArray inner)
                    {
                        continue;
                    }

                    ours.ForEach(h => inner.Remove(h));
                    changed = true;

                    // Prune only what we emptied. The user's own empty groups stay.
                    if (inner.Count == 0)
                    {
                        groups.Remove(group);
                        removedGroup = true;
                    }
                }

                if (removedGroup && groups.Count == 0)
                {
                    hooks.Remove(name);
                }
            }
        }

        if (IsOurStatusLine(root["statusLine"]))
        {
            root.Remove("statusLine");
            changed = true;
        }

        return changed ? Save(settingsPath, root, now) : new Result(false, null, null);
    }

    /// <summary>The parts we touch must have the shape Claude Code expects. Anything else: refuse, do not repair.</summary>
    private static string? Shape(JsonObject root)
    {
        if (root["hooks"] is { } hooks && hooks is not JsonObject)
        {
            return "\"hooks\" is not an object.";
        }

        if (root["hooks"] is JsonObject events)
        {
            foreach (var (name, node) in events)
            {
                if (ClaudeHookMapper.Kinds.ContainsKey(name) && node is not null && node is not JsonArray)
                {
                    return $"\"hooks.{name}\" is not an array.";
                }
            }
        }

        return null;
    }

    private static JsonObject Group(string hookName, string baseUrl)
    {
        var group = new JsonObject();
        if (ClaudeHookMapper.ToolHooks.Contains(hookName))
        {
            group["matcher"] = "*";
        }

        group["hooks"] = new JsonArray(new JsonObject
        {
            ["type"] = "http",
            ["url"] = HookUrl(baseUrl),
            ["timeout"] = HookTimeoutSeconds,
        });
        return group;
    }

    /// <summary>Our hook entries inside one group. Tolerates any shape: odd entries are simply not ours.</summary>
    private static IEnumerable<JsonNode> Ours(JsonNode? group, string url) =>
        group is JsonObject g && g["hooks"] is JsonArray hooks
            ? hooks.OfType<JsonObject>().Where(h => Str(h, "type") == "http" && Str(h, "url") == url)
            : [];

    private static bool IsOurStatusLine(JsonNode? statusLine) =>
        statusLine is JsonObject o && Str(o, "command") == StatusLineCommand;

    private static string? Str(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static (JsonObject? Root, string? Error) Load(string path)
    {
        var info = new FileInfo(path);
        if (info.LinkTarget is not null && info.ResolveLinkTarget(returnFinalTarget: true) is not { Exists: true })
        {
            return (null, $"{path} is a symlink to a missing file. Nothing was changed.");
        }

        if (!File.Exists(path))
        {
            return ([], null);
        }

        try
        {
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return ([], null);
            }

            // Comments are refused, not skipped: skipping would drop them silently on write.
            var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = true };
            if (JsonNode.Parse(text, documentOptions: options) is not JsonObject root)
            {
                return (null, $"{path} is not a JSON object. Nothing was changed.");
            }

            Touch(root); // JsonObject finds duplicate keys lazily; surface them here, before any change.
            return (root, null);
        }
        catch (JsonException ex)
        {
            return (null, $"{path} could not be read as plain JSON: {ex.Message} (Comments are not supported.) Nothing was changed.");
        }
        catch (ArgumentException ex)
        {
            // JsonObject throws this for a property name that appears twice.
            return (null, $"{path} has a duplicate key: {ex.Message} Nothing was changed.");
        }
    }

    private static void Touch(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (_, child) in o)
                {
                    Touch(child);
                }

                break;
            case JsonArray a:
                foreach (var child in a)
                {
                    Touch(child);
                }

                break;
        }
    }

    /// <summary>
    /// Backup first, then write a temp file next to the real target and move it over, so a crash never
    /// leaves half a file. A symlinked settings.json is written through to its target, and on Unix the
    /// original file mode is kept.
    /// </summary>
    private static Result Save(string path, JsonObject root, DateTimeOffset now)
    {
        var target = Path.GetFullPath(path);
        string? backup = null;
        UnixFileMode? mode = null;
        if (File.Exists(target))
        {
            if (new FileInfo(target).ResolveLinkTarget(returnFinalTarget: true) is { } link)
            {
                target = link.FullName;
            }

            if (!OperatingSystem.IsWindows())
            {
                mode = File.GetUnixFileMode(target);
            }

            backup = $"{target}.ancos-santiye-{now:yyyyMMdd-HHmmss-fff}.bak";
            for (var n = 2; File.Exists(backup); n++)
            {
                backup = $"{target}.ancos-santiye-{now:yyyyMMdd-HHmmss-fff}-{n}.bak";
            }

            File.Copy(target, backup, overwrite: false);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        }

        var temp = $"{target}.ancos-santiye-{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temp, Print(root) + Environment.NewLine);
        if (mode is { } m && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temp, m);
        }

        File.Move(temp, target, overwrite: true);
        return new Result(true, backup, null);
    }
}
