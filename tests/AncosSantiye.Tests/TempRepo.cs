using AncosSantiye.Actions;

namespace AncosSantiye.Tests;

/// <summary>A real git repo in a temp folder, with one commit, removed afterwards.</summary>
public sealed class TempRepo : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "santiye-git-" + Guid.NewGuid());

    public string Dir => Path.Combine(Root, "repo");

    public TempRepo()
    {
        Directory.CreateDirectory(Dir);
        Git("init", "-q", "-b", "main");
        Git("config", "user.name", "Test");
        Git("config", "user.email", "test@example.com");
        Git("config", "commit.gpgsign", "false");
        Write("readme.md", "hello");
        Git("add", "--", "readme.md");
        Git("commit", "-q", "-m", "first");
    }

    public void Write(string path, string text)
    {
        var full = Path.Combine(Dir, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    public string Git(params string[] args)
    {
        var result = Actions.Git.Run(Dir, TimeSpan.FromSeconds(30), args).GetAwaiter().GetResult();
        Assert.True(result.Ok, result.Output);
        return result.Stdout;
    }

    public int CommitCount() => int.Parse(Git("rev-list", "--count", "HEAD").Trim());

    public void Dispose()
    {
        // git marks object files read only; clear that so the folder can go on Windows too.
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(Root, recursive: true);
    }
}
