using AncosSantiye.Profiles;

namespace AncosSantiye.Tests;

public sealed class ProfileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "santiye-profile-" + Guid.NewGuid());

    public ProfileTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string json)
    {
        var path = Path.Combine(_dir, "p.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void NoPath_IsTheDefault()
    {
        var (profile, error) = Profile.Load(null);

        Assert.Null(error);
        Assert.Equal("grafxkid", profile!.Skin);
        Assert.Null(profile.FolderLane);
        Assert.False(profile.ActionsEnabled);
    }

    [Fact]
    public void TheShippedExample_Loads()
    {
        var example = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "profiles", "example-orchestra.json");

        var (profile, error) = Profile.Load(example);

        Assert.Null(error);
        Assert.Equal("my-project", profile!.FolderLane!.Project);
        Assert.False(profile.ActionsEnabled);
    }

    [Fact]
    public void ActionsStayOffUnlessSaidSo()
    {
        var (profile, _) = Profile.Load(Write("""{ "actions": {} }"""));

        Assert.False(profile!.ActionsEnabled);
    }

    [Theory]
    [InlineData("*-durum.md", "rovans-durum.md", "rovans")]
    [InlineData("*-durum.md", "kbyte-preview-durum.md", "kbyte-preview")]
    [InlineData("*-durum.md", "ROVANS-DURUM.MD", "ROVANS")]
    [InlineData("*-durum.md", "-durum.md", null)]
    [InlineData("*-durum.md", "  -durum.md", null)]
    [InlineData("*-durum.md", "rovans-001-check.log", null)]
    [InlineData("*-durum.md", "rovans-durum.md.bak", null)]
    [InlineData("status-*.md", "status-kbyte.md", "kbyte")]
    [InlineData("x-durum.md", "x-durum.md", "x")]
    [InlineData("x-durum.md", "y-durum.md", null)]
    public void ProjectOf_NamesTheProjectFromTheFileName(string statusFile, string fileName, string? project)
    {
        var lane = new FolderLaneProfile("d", statusFile, statusFile.Contains('*') ? null : "x");

        Assert.Equal(project, lane.ProjectOf(fileName));
    }

    [Fact]
    public void UpdateChecks_AreOnUnlessTurnedOff()
    {
        Assert.True(Profile.Load(Write("{}")).Item1!.UpdateCheck);
        Assert.False(Profile.Load(Write("""{ "updateCheck": false }""")).Item1!.UpdateCheck);
    }

    [Fact]
    public void APattern_NeedsNoProject()
    {
        var (profile, error) = Profile.Load(Write("""{ "folderLane": { "directory": "d", "statusFile": "*-durum.md" } }"""));

        Assert.Null(error);
        Assert.True(profile!.FolderLane!.IsPattern);
    }

    [Theory]
    [InlineData("""{ "folderLane": { "directory": "d", "statusFile": "s.md" } }""", "folderLane needs")]
    [InlineData("""{ "folderLane": { "directory": "d", "statusFile": "s.md", "project": "p", "intervalSeconds": 0 } }""", "intervalSeconds")]
    [InlineData("""{ "folderLane": { "directory": "d", "statusFile": "*-*.md" } }""", "at most one *")]
    [InlineData("""{ "folderLane": { "directory": "d", "statusFile": "../*-durum.md" } }""", "at most one *")]
    [InlineData("""{ "skin": """, "not valid JSON")]
    [InlineData("""{ "actions": { "enabled": true } }""", "actions.repos")]
    [InlineData("""{ "actions": { "enabled": true, "repos": [] } }""", "actions.repos")]
    public void BadProfile_IsAnErrorNotADefault(string json, string message)
    {
        var (profile, error) = Profile.Load(Write(json));

        Assert.Null(profile);
        Assert.Contains(message, error);
    }

    [Fact]
    public void RelativeFolders_StartAtTheProfileFile()
    {
        var (profile, error) = Profile.Load(Write("""
            { "skinPath": "skin", "folderLane": { "directory": "../logs", "statusFile": "s.md", "project": "p" },
              "actions": { "enabled": true, "repos": ["repo", "C:/abs/repo"] } }
            """));

        Assert.Null(error);
        Assert.Equal(Path.Combine(_dir, "skin"), profile!.SkinPath);
        Assert.Equal(Path.GetFullPath(Path.Combine(_dir, "..", "logs")), profile.FolderLane!.Directory);
        Assert.Equal(Path.Combine(_dir, "repo"), profile.Actions!.Repos![0]);
    }

    [Fact]
    public void MissingFile_IsAnError()
    {
        var (profile, error) = Profile.Load(Path.Combine(_dir, "nope.json"));

        Assert.Null(profile);
        Assert.Contains("not found", error);
    }

    [Fact]
    public void MissingSkin_StopsTheServerWithAClearMessage()
    {
        var ex = Assert.Throws<SiteApp.SetupException>(() =>
            SiteApp.Build([], _dir, "http://127.0.0.1:0", new Profile(SkinPath: Path.Combine(_dir, "noskin"))));

        Assert.Contains("Skin not found", ex.Message);
    }
}
