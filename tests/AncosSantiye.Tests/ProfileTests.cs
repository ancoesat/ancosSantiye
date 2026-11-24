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
    [InlineData("""{ "folderLane": { "directory": "d", "statusFile": "s.md" } }""", "folderLane needs")]
    [InlineData("""{ "folderLane": { "directory": "d", "statusFile": "s.md", "project": "p", "intervalSeconds": 0 } }""", "intervalSeconds")]
    [InlineData("""{ "skin": """, "not valid JSON")]
    public void BadProfile_IsAnErrorNotADefault(string json, string message)
    {
        var (profile, error) = Profile.Load(Write(json));

        Assert.Null(profile);
        Assert.Contains(message, error);
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
