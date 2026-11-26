using System.Text.Json.Nodes;
using AncosSantiye.Adapters;

namespace AncosSantiye.Setup;

/// <summary>
/// Antigravity CLI (agy) hooks in the user-level <c>~/.gemini/config/hooks.json</c>, so every agy run on this
/// machine reports, in any folder. Only our own top-level key is written; the user's hooks stay as they are.
/// Same safety as the Claude Code installer: backup first, refuse a file it cannot fully read.
/// </summary>
public static class AgyHooks
{
    public const string Key = "ancos-santiye";

    /// <summary>Seconds. agy waits for a hook, so keep it short; the command itself gives up after 1.5 s.</summary>
    public const int TimeoutSeconds = 5;

    public static string PathIn(string home) => Path.Combine(home, ".gemini", "config", "hooks.json");

    public static string Command(string eventName) => $"{HookInstaller.HookCommand} --from agy --event {eventName}";

    public static JsonObject Entry()
    {
        var entry = new JsonObject();
        foreach (var name in AgyHookMapper.Events)
        {
            var group = new JsonObject();
            if (AgyHookMapper.ToolEvents.Contains(name))
            {
                group["matcher"] = ".*";
            }

            group["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = Command(name), ["timeout"] = TimeoutSeconds });
            entry[name] = new JsonArray(group);
        }

        return entry;
    }

    public static HookInstaller.Result Install(string path, DateTimeOffset now)
    {
        var (root, error) = HookInstaller.Load(path);
        if (root is null)
        {
            return new HookInstaller.Result(false, null, error);
        }

        if (JsonNode.DeepEquals(root[Key], Entry()))
        {
            return new HookInstaller.Result(false, null, null);
        }

        root[Key] = Entry();
        return HookInstaller.Save(path, root, now);
    }

    public static HookInstaller.Result Uninstall(string path, DateTimeOffset now)
    {
        if (!File.Exists(path))
        {
            return new HookInstaller.Result(false, null, null);
        }

        var (root, error) = HookInstaller.Load(path);
        if (root is null)
        {
            return new HookInstaller.Result(false, null, error);
        }

        return root.Remove(Key) ? HookInstaller.Save(path, root, now) : new HookInstaller.Result(false, null, null);
    }

    /// <summary>Connected, partly (ours is there but out of date), not connected, or a note why it can't tell.</summary>
    public static (string Status, IReadOnlyList<string> Notes) Check(string home)
    {
        var path = PathIn(home);
        if (!File.Exists(path))
        {
            return (ToolStatus.NotConnected, ["No ~/.gemini/config/hooks.json yet. Run: ancos-santiye connect agy"]);
        }

        var (root, _) = HookInstaller.Load(path);
        return root switch
        {
            null => (ToolStatus.NotConnected, ["~/.gemini/config/hooks.json is not plain JSON, so it cannot be checked."]),
            _ when JsonNode.DeepEquals(root[Key], Entry()) => (ToolStatus.Connected, ["Hooks in ~/.gemini/config/hooks.json (every folder)."]),
            _ when root[Key] is not null => (ToolStatus.Partly, ["Our hooks are from another version. Run: ancos-santiye connect agy"]),
            _ => (ToolStatus.NotConnected, ["Run: ancos-santiye connect agy"]),
        };
    }
}

/// <summary>
/// A small OpenCode plugin in the user-level plugin folder, so the terminal and the desktop app both load it.
/// We own the whole file and mark its first line; a file without our mark is never overwritten or deleted.
/// </summary>
public static class OpenCodePlugin
{
    public const string Mark = "// ancos-santiye plugin";

    public static string PathIn(string home) => Path.Combine(home, ".config", "opencode", "plugins", "ancos-santiye.js");

    /// <summary>
    /// Sends only: the event kind, session id, project folder name, tool name, a file's last segment, the
    /// short description of a bash or task call, and todo titles with their status. Never arguments, commands or output.
    /// Works with both plugin loaders: older ones call the exported function, newer ones call its <c>server</c>.
    /// </summary>
    public static string Source(string baseUrl) => $$"""
        {{Mark}} (written by `ancos-santiye connect opencode`, removed by `ancos-santiye disconnect opencode`)
        // Tells the local ancosSantiye site what OpenCode is doing: tool names, file names, todo titles. Nothing else.
        const URL = {{System.Text.Json.JsonSerializer.Serialize(baseUrl.TrimEnd('/') + "/hooks/opencode")}};
        const last = (p) => (typeof p === "string" ? p.split(/[\\/]/).filter(Boolean).pop() : undefined);
        const cut = (s) => (typeof s === "string" ? s.split("\n")[0].slice(0, 60) : undefined);
        const send = (body) => {
          try {
            fetch(URL, { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body) }).catch(() => {});
          } catch {}
        };
        const sessionOf = (p) => (p && (p.sessionID || (p.info && p.info.id))) || undefined;

        async function AncosSantiye(ctx = {}) {
          const project = last(ctx.directory || ctx.worktree);
          return {
            "tool.execute.before": async (input, output) => {
              const args = (output && output.args) || {};
              // The description is the agent's own short label for a command or a helper. Other tools' args stay here.
              const description = input.tool === "bash" || input.tool === "task" ? cut(args.description) : undefined;
              send({ kind: "tool.start", project, session: input.sessionID, tool: input.tool,
                     file: last(args.filePath || args.path), description });
            },
            "tool.execute.after": async (input) => {
              send({ kind: "tool.end", project, session: input.sessionID, tool: input.tool });
            },
            event: async ({ event }) => {
              const p = (event && event.properties) || {};
              const session = sessionOf(p);
              if (!session) return;
              if (event.type === "session.created") send({ kind: "agent.start", project, session });
              else if (event.type === "session.idle") send({ kind: "turn.stop", project, session });
              else if (event.type === "session.deleted") send({ kind: "agent.stop", project, session });
              else if (event.type === "todo.updated" && Array.isArray(p.todos)) {
                const todos = p.todos.slice(0, 12).map((t) => ({ content: cut(t.content), status: t.status }));
                send({ kind: "tool.start", project, session, tool: "todowrite", todos });
                send({ kind: "tool.end", project, session, tool: "todowrite" });
              }
            },
          };
        }

        AncosSantiye.id = "ancos-santiye";
        AncosSantiye.server = AncosSantiye;
        export default AncosSantiye;

        """;

    public static HookInstaller.Result Install(string path, string baseUrl)
    {
        var source = Source(baseUrl);
        if (File.Exists(path))
        {
            var current = File.ReadAllText(path);
            if (!current.StartsWith(Mark, StringComparison.Ordinal))
            {
                return new HookInstaller.Result(false, null, $"{path} exists and was not written by ancosSantiye. Nothing was changed.");
            }

            if (current == source)
            {
                return new HookInstaller.Result(false, null, null);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, source);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp); // no-op after the move
        }

        return new HookInstaller.Result(true, null, null);
    }

    public static HookInstaller.Result Uninstall(string path)
    {
        if (!File.Exists(path))
        {
            return new HookInstaller.Result(false, null, null);
        }

        if (!File.ReadAllText(path).StartsWith(Mark, StringComparison.Ordinal))
        {
            return new HookInstaller.Result(false, null, $"{path} was not written by ancosSantiye. Nothing was changed.");
        }

        File.Delete(path);
        return new HookInstaller.Result(true, null, null);
    }

    public static (string Status, IReadOnlyList<string> Notes) Check(string home, string baseUrl)
    {
        var path = PathIn(home);
        if (!File.Exists(path))
        {
            return (ToolStatus.NotConnected, ["Run: ancos-santiye connect opencode"]);
        }

        var text = File.ReadAllText(path);
        return text == Source(baseUrl)
            ? (ToolStatus.Connected, ["Plugin in ~/.config/opencode/plugins (terminal and desktop app). Restart OpenCode once after connecting."])
            : text.StartsWith(Mark, StringComparison.Ordinal)
                ? (ToolStatus.Partly, ["Our plugin is from another version or address. Run: ancos-santiye connect opencode"])
                : (ToolStatus.NotConnected, ["~/.config/opencode/plugins/ancos-santiye.js is someone else's file, so it is left alone."]);
    }
}

/// <summary>The three tools ancosSantiye can connect to, behind one door for the CLI and the setup page.</summary>
public static class Connect
{
    public static readonly IReadOnlyList<string> Tools = ["claude-code", "agy", "opencode"];

    /// <summary>Null for a tool we cannot connect to. A file that cannot be read or written is an error, not a crash.</summary>
    public static HookInstaller.Result? Run(string tool, bool connect, string home, string baseUrl, DateTimeOffset now)
    {
        // .NET gives "" when HOME is unset or missing; Path.Combine would then write into the current folder.
        if (!Path.IsPathRooted(home) || !Directory.Exists(home))
        {
            return Tools.Contains(tool) ? new HookInstaller.Result(false, null, $"Home folder not found ('{home}'). Nothing was changed.") : null;
        }

        try
        {
            return Unguarded(tool, connect, home, baseUrl, now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new HookInstaller.Result(false, null, $"{ex.Message} Nothing was changed.");
        }
    }

    private static HookInstaller.Result? Unguarded(string tool, bool connect, string home, string baseUrl, DateTimeOffset now)
    {
        var claude = Path.Combine(home, ".claude", "settings.json");
        return (tool, connect) switch
        {
            ("claude-code", true) => HookInstaller.Install(claude, baseUrl, now),
            ("claude-code", false) => HookInstaller.Uninstall(claude, baseUrl, now),
            ("agy", true) => AgyHooks.Install(AgyHooks.PathIn(home), now),
            ("agy", false) => AgyHooks.Uninstall(AgyHooks.PathIn(home), now),
            ("opencode", true) => OpenCodePlugin.Install(OpenCodePlugin.PathIn(home), baseUrl),
            ("opencode", false) => OpenCodePlugin.Uninstall(OpenCodePlugin.PathIn(home)),
            _ => null,
        };
    }
}
