using AncosSantiye.Actions;
using AncosSantiye.Events;

namespace AncosSantiye.Tests;

public sealed class GitActionsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    private readonly TempRepo _repo = new();
    private readonly GitActions _actions;

    public GitActionsTests() => _actions = new GitActions([_repo.Dir]);

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task Status_ListsChangedNewAndDeletedFiles()
    {
        _repo.Write("readme.md", "changed");
        _repo.Write("src/new.cs", "x");

        var files = await _actions.Status(_repo.Dir);

        Assert.Equal([" M readme.md", "?? src/new.cs"], files!.Select(f => f.Code + " " + f.Path).Order());
    }

    [Fact]
    public async Task Commit_TakesOnlyThePickedFiles_EvenWithOthersStaged()
    {
        _repo.Write("a.txt", "a");
        _repo.Write("b.txt", "b");
        _repo.Git("add", "--", "b.txt");

        var outcome = await _actions.Commit(0, ["a.txt"], "add a", Now);

        Assert.True(outcome!.Ok, outcome.Message);
        Assert.Equal("a.txt", _repo.Git("show", "--name-only", "--format=", "HEAD").Trim());
        Assert.Contains("b.txt", _repo.Git("diff", "--cached", "--name-only"));
    }

    [Fact]
    public async Task Commit_KeepsTheMessageExactly()
    {
        _repo.Write("a.txt", "a");
        const string message = "Say \"hi\" & 'bye'\n\n$(whoami) `id` ; rm -rf /";

        var outcome = await _actions.Commit(0, ["a.txt"], message, Now);

        Assert.True(outcome!.Ok, outcome.Message);
        Assert.Equal(message, _repo.Git("log", "-1", "--format=%B").TrimEnd('\n'));
    }

    [Fact]
    public async Task Commit_OfADeletedFile_Works()
    {
        File.Delete(Path.Combine(_repo.Dir, "readme.md"));

        var outcome = await _actions.Commit(0, ["readme.md"], "remove readme", Now);

        Assert.True(outcome!.Ok, outcome.Message);
        Assert.Empty(_repo.Git("ls-files").Trim());
    }

    [Fact]
    public async Task Commit_TreatsAFileNameAsAName_NotAPattern()
    {
        // "[ab].txt" as a pattern would also match a.txt. Brackets are legal file names on Windows too.
        _repo.Write("[ab].txt", "x");
        _repo.Write("a.txt", "a");

        var outcome = await _actions.Commit(0, ["[ab].txt"], "brackets", Now);

        Assert.True(outcome!.Ok, outcome.Message);
        Assert.Equal("[ab].txt", _repo.Git("show", "--name-only", "--format=", "HEAD").Trim());
        Assert.Contains("?? a.txt", _repo.Git("status", "--porcelain"));
    }

    [Fact]
    public async Task Commit_OfADeletedPatternLikeName_TouchesNothingElse()
    {
        // With the file gone, git would read "[ab].txt" as a pattern and stage a.txt along with it.
        _repo.Write("[ab].txt", "x");
        _repo.Write("a.txt", "a");
        _repo.Git("add", "--", "[ab].txt", "a.txt");
        _repo.Git("commit", "-q", "-m", "both");
        File.Delete(Path.Combine(_repo.Dir, "[ab].txt"));
        _repo.Write("a.txt", "changed");

        var outcome = await _actions.Commit(0, ["[ab].txt"], "remove brackets", Now);

        Assert.True(outcome!.Ok, outcome.Message);
        Assert.Equal(" M a.txt", _repo.Git("status", "--porcelain").TrimEnd('\n'));
    }

    private void Hook(string body)
    {
        var hook = Path.Combine(_repo.Dir, ".git", "hooks", "pre-commit");
        File.WriteAllText(hook, "#!/bin/sh\n" + body + "\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task RefusedCommit_LeavesTheIndexAsItWas()
    {
        Hook("echo no from hook; exit 1");
        _repo.Write("a.txt", "a");
        _repo.Write("b.txt", "b");
        _repo.Git("add", "--", "b.txt");

        var outcome = await _actions.Commit(0, ["a.txt", "b.txt"], "refused", Now);

        Assert.False(outcome!.Ok);
        Assert.Contains("no from hook", outcome.Message);
        Assert.Equal("A  b.txt\n?? a.txt", _repo.Git("status", "--porcelain").TrimEnd('\n'));
        Assert.Equal(1, _repo.CommitCount());
    }

    [Fact]
    public async Task Git_TimeLimitCoversAChildThatKeepsTheOutputOpen()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var result = await Git.Run(_repo.Dir, TimeSpan.FromSeconds(1), "-c", "alias.slow=!sleep 4 & echo started", "slow");

        Assert.Equal(-1, result.ExitCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task OnlyOneActionRunsAtATime()
    {
        Hook("sleep 2");
        _repo.Write("a.txt", "a");
        _repo.Write("b.txt", "b");

        var first = _actions.Commit(0, ["a.txt"], "slow", Now);
        await Task.Delay(500);
        var second = await _actions.Commit(0, ["b.txt"], "second", Now);
        var push = await _actions.Push(0, Now);

        Assert.Null(second);
        Assert.Null(push);
        Assert.True((await first)!.Ok);
    }

    [Theory]
    [InlineData("-A")]
    [InlineData(".")]
    [InlineData("*")]
    [InlineData("..")]
    [InlineData("../outside.txt")]
    [InlineData(":/")]
    [InlineData("readme.md")]
    public async Task Commit_RefusesAnythingNotInStatus(string path)
    {
        _repo.Write("a.txt", "a");

        var outcome = await _actions.Commit(0, [path], "nope", Now);

        Assert.False(outcome!.Ok);
        Assert.Contains("Not in git status", outcome.Message);
        Assert.Equal(1, _repo.CommitCount());
    }

    [Fact]
    public async Task Commit_RefusesAnAbsolutePath()
    {
        _repo.Write("a.txt", "a");

        var outcome = await _actions.Commit(0, [Path.Combine(_repo.Dir, "a.txt")], "nope", Now);

        Assert.False(outcome!.Ok);
        Assert.Equal(1, _repo.CommitCount());
    }

    [Theory]
    [InlineData(new string[0], "msg")]
    [InlineData(new[] { "a.txt", "a.txt" }, "msg")]
    [InlineData(new[] { "a.txt" }, "   ")]
    [InlineData(new[] { "a.txt" }, null)]
    [InlineData(new[] { "a.txt" }, "bad\0byte")]
    public async Task Commit_RefusesBadInput(string[] paths, string? message)
    {
        _repo.Write("a.txt", "a");

        var outcome = await _actions.Commit(0, paths, message, Now);

        Assert.False(outcome!.Ok);
        Assert.Equal(1, _repo.CommitCount());
    }

    [Fact]
    public async Task Commit_RefusesATooLongMessage()
    {
        _repo.Write("a.txt", "a");

        var outcome = await _actions.Commit(0, ["a.txt"], new string('x', GitActions.MaxMessageLength + 1), Now);

        Assert.False(outcome!.Ok);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task UnknownRepoIndex_IsRefused(int index)
    {
        _repo.Write("a.txt", "a");

        Assert.False((await _actions.Commit(index, ["a.txt"], "m", Now))!.Ok);
        Assert.False((await _actions.Push(index, Now))!.Ok);
    }

    [Fact]
    public async Task Commit_GivesAnEventWithoutFileNamesOrContent()
    {
        _repo.Write("secret-plan.txt", "the content");

        var outcome = await _actions.Commit(0, ["secret-plan.txt"], "plan", Now);

        var e = outcome!.Event!;
        Assert.Equal(EventKinds.ActionCommit, e.Kind);
        Assert.Equal(Sources.Screen, e.Source);
        Assert.Equal("plan", e.Data!["title"]);
        Assert.Equal("1", e.Data["files"]);
        Assert.DoesNotContain(e.Data.Values, v => v.Contains("secret") || v.Contains("content"));
    }

    private async Task<string> BareRemote(string name)
    {
        var remote = Path.Combine(_repo.Root, name + ".git");
        Directory.CreateDirectory(remote);
        Assert.True((await Git.Run(remote, TimeSpan.FromSeconds(30), "init", "-q", "--bare", "-b", "main")).Ok);
        _repo.Git("remote", "add", name, remote);
        return remote;
    }

    [Fact]
    public async Task Push_NamesWhereAPlainPushReallyGoes()
    {
        await BareRemote("origin");
        _repo.Git("push", "-q", "-u", "origin", "main");
        var backup = await BareRemote("backup");
        _repo.Git("push", "-q", "backup", "main");
        _repo.Git("config", "remote.pushDefault", "backup");
        Assert.Contains("Cannot tell where", (await _actions.Push(0, Now))!.Message);
        _repo.Git("config", "push.default", "current");
        _repo.Write("a.txt", "a");
        await _actions.Commit(0, ["a.txt"], "add a", Now);

        var outcome = await _actions.Push(0, Now);

        Assert.True(outcome!.Ok, outcome.Message);
        Assert.Equal("backup/main", outcome.Event!.Data!["upstream"]);
        Assert.Equal(_repo.Git("rev-parse", "HEAD"), (await Git.Run(backup, TimeSpan.FromSeconds(30), "rev-parse", "main")).Stdout);
    }

    [Fact]
    public async Task Push_WithoutUpstream_SaysHowToSetIt()
    {
        var outcome = await _actions.Push(0, Now);

        Assert.False(outcome!.Ok);
        Assert.Contains("Cannot tell where", outcome.Message);
    }

    [Fact]
    public async Task Push_SendsTheCommitToTheUpstream()
    {
        var remote = Path.Combine(_repo.Root, "remote.git");
        Directory.CreateDirectory(remote);
        Assert.True((await Git.Run(remote, TimeSpan.FromSeconds(30), "init", "-q", "--bare", "-b", "main")).Ok);
        _repo.Git("remote", "add", "origin", remote);
        _repo.Git("push", "-q", "-u", "origin", "main");
        _repo.Write("a.txt", "a");
        await _actions.Commit(0, ["a.txt"], "add a", Now);

        var outcome = await _actions.Push(0, Now);

        Assert.True(outcome!.Ok, outcome.Message);
        Assert.Equal(_repo.Git("rev-parse", "HEAD"), (await Git.Run(remote, TimeSpan.FromSeconds(30), "rev-parse", "main")).Stdout);
        Assert.Equal(EventKinds.ActionPush, outcome.Event!.Kind);
        Assert.Equal("origin/main", outcome.Event.Data!["upstream"]);

        var again = await _actions.Push(0, Now);

        Assert.True(again!.Ok);
        Assert.Contains("Nothing to push", again.Message);
        Assert.Null(again.Event);
    }
}
