using AncosSantiye.Events;

namespace AncosSantiye.Actions;

public sealed record ChangedFile(string Code, string Path);

public sealed record ActionOutcome(bool Ok, string Message, SiteEvent? Event = null);

/// <summary>
/// Commit and push for the repos the profile lists, and nothing else. Every path must be one git status
/// reports right now; there is no "all", no wildcard, no branch, no force.
/// </summary>
public sealed class GitActions(IReadOnlyList<string> repos)
{
    public const int MaxMessageLength = 10_000;
    public const int MaxFiles = 500;

    private static readonly TimeSpan Short = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(120);
    private static readonly Actor You = new("you", Roles.Chief, "You");

    private readonly SemaphoreSlim _one = new(1, 1);

    public IReadOnlyList<string> Names { get; } = repos.Select(r => System.IO.Path.GetFileName(r.TrimEnd('/', '\\'))).ToArray();

    /// <summary>The repo for an index from the page, or null. The page never sends a path.</summary>
    public string? RepoAt(int index) => index >= 0 && index < repos.Count ? repos[index] : null;

    public async Task<IReadOnlyList<ChangedFile>?> Status(string repo)
    {
        // -z: names as they are, no quoting. --no-renames: a rename is a delete plus an add, both listed.
        var result = await Git.Run(repo, Short, "status", "--porcelain=v1", "-z", "--no-renames", "--untracked-files=all");
        if (!result.Ok)
        {
            return null;
        }

        return result.Stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(entry => entry.Length > 3)
            .Select(entry => new ChangedFile(entry[..2], entry[3..]))
            .ToArray();
    }

    /// <summary>Returns null while another action runs: one git command at a time per server.</summary>
    public async Task<ActionOutcome?> Commit(int repoIndex, IReadOnlyList<string>? paths, string? message, DateTimeOffset now)
    {
        if (RepoAt(repoIndex) is not { } repo)
        {
            return new(false, "Unknown repo.");
        }

        var text = message?.Trim() ?? "";
        if (text.Length == 0 || text.Length > MaxMessageLength || text.Contains('\0'))
        {
            return new(false, $"The message must be 1 to {MaxMessageLength} characters.");
        }

        if (paths is not { Count: > 0 and <= MaxFiles } || paths.Distinct(StringComparer.Ordinal).Count() != paths.Count)
        {
            return new(false, $"Pick 1 to {MaxFiles} files, each once.");
        }

        if (!await _one.WaitAsync(0))
        {
            return null;
        }

        string? messageFile = null;
        try
        {
            var status = await Status(repo);
            if (status is null)
            {
                return new(false, "git status failed in this folder.");
            }

            var known = status.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
            var unknown = paths.FirstOrDefault(p => !known.Contains(p));
            if (unknown is not null)
            {
                return new(false, $"Not in git status anymore: {unknown}. Refresh and pick again.");
            }

            // Picked files that were not staged before. If the commit fails they are unstaged again,
            // so a refused commit leaves the index as it was.
            var unstagedBefore = status.Where(f => f.Code[0] is ' ' or '?' && paths.Contains(f.Path)).Select(f => f.Path).ToArray();

            var add = await Git.Run(repo, Short, ["add", "--", .. paths]);
            if (!add.Ok)
            {
                return new(false, add.Output);
            }

            // -F reads the message from a file: quotes and new lines stay as written. The paths after --
            // make it a commit of these files only, even if other changes are staged.
            messageFile = System.IO.Path.GetTempFileName();
            await File.WriteAllTextAsync(messageFile, text);
            var commit = await Git.Run(repo, Long, ["commit", "-F", messageFile, "--", .. paths]);
            if (!commit.Ok)
            {
                if (unstagedBefore.Length > 0)
                {
                    var reset = await Git.Run(repo, Short, ["reset", "-q", "--", .. unstagedBefore]);
                    if (!reset.Ok)
                    {
                        return new(false, commit.Output + "\n\nThe picked files are still staged (git reset failed).");
                    }
                }

                return new(false, commit.Output);
            }

            var head = await Git.Run(repo, Short, "rev-parse", "--short=12", "HEAD");
            var hash = head.Ok ? head.Stdout.Trim() : "unknown";
            var title = text.Split('\n')[0].Trim();
            var e = SiteEvent.Create(
                Sources.Screen,
                Names[repoIndex],
                You,
                EventKinds.ActionCommit,
                data: new Dictionary<string, string> { ["hash"] = hash, ["title"] = title, ["files"] = paths.Count.ToString() },
                ts: now);
            return new(true, $"{hash} {title}", e);
        }
        finally
        {
            if (messageFile is not null)
            {
                File.Delete(messageFile);
            }

            _one.Release();
        }
    }

    /// <summary>Plain <c>git push</c> to the branch's upstream. No arguments, so no force and no other branch.</summary>
    public async Task<ActionOutcome?> Push(int repoIndex, DateTimeOffset now)
    {
        if (RepoAt(repoIndex) is not { } repo)
        {
            return new(false, "Unknown repo.");
        }

        if (!await _one.WaitAsync(0))
        {
            return null;
        }

        try
        {
            // @{push} is where a plain `git push` really goes (push.default, pushRemote), not only @{u}.
            // If git cannot name it, we do not push: the screen never pushes somewhere it cannot show.
            var where = await Git.Run(repo, Short, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{push}");
            if (!where.Ok)
            {
                return new(false, "Cannot tell where a plain git push goes from this branch (no upstream, or push settings point elsewhere). Push once in a terminal, for example: git push -u origin <branch>");
            }

            var target = where.Stdout.Trim();
            var ahead = await Git.Run(repo, Short, "rev-list", "--count", "@{push}..HEAD");
            if (ahead.Ok && ahead.Stdout.Trim() == "0")
            {
                return new(true, $"Nothing to push, {target} is up to date.");
            }

            var push = await Git.Run(repo, Long, "push");
            if (!push.Ok)
            {
                return new(false, push.Output);
            }

            var e = SiteEvent.Create(
                Sources.Screen,
                Names[repoIndex],
                You,
                EventKinds.ActionPush,
                data: new Dictionary<string, string> { ["upstream"] = target },
                ts: now);
            return new(true, $"Pushed to {target}", e);
        }
        finally
        {
            _one.Release();
        }
    }
}
