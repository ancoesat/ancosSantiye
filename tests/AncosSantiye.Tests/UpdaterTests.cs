using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AncosSantiye.Updates;

namespace AncosSantiye.Tests;

public sealed class UpdaterTests : IDisposable
{
    private const string Download = "https://github.com/ancoesat/ancosSantiye/releases/download/v9.1.0/AncosSantiye.9.1.0.nupkg";
    private static readonly string Hash = new('a', 64);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "santiye-update-" + Guid.NewGuid());

    public UpdaterTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Json(string tag = "v9.1.0", string name = "AncosSantiye.9.1.0.nupkg", string url = Download,
        string? digest = null, bool draft = false, bool prerelease = false) => JsonSerializer.Serialize(new
        {
            tag_name = tag,
            html_url = "https://github.com/ancoesat/ancosSantiye/releases/tag/" + tag,
            draft,
            prerelease,
            assets = new[] { new { name, browser_download_url = url, digest = digest ?? "sha256:" + Hash } },
        });

    private static Release? FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Releases.FromJson(doc.RootElement);
    }

    [Fact]
    public void AGoodRelease_IsRead()
    {
        var r = FromJson(Json())!;

        Assert.Equal(new Version(9, 1, 0), r.Version);
        Assert.Equal(Download, r.PackageUrl);
        Assert.Equal(Hash, r.Sha256);
        Assert.StartsWith("https://github.com/ancoesat/ancosSantiye/", r.PageUrl);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("prerelease")]
    [InlineData("tag")]
    [InlineData("name")]
    [InlineData("otherRepo")]
    [InlineData("otherHost")]
    [InlineData("noDigest")]
    [InlineData("md5")]
    [InlineData("shortDigest")]
    public void AReleaseTheUpdaterCannotTrust_IsIgnored(string what)
    {
        var json = what switch
        {
            "draft" => Json(draft: true),
            "prerelease" => Json(prerelease: true),
            "tag" => Json(tag: "v9.1.0-beta"),
            "name" => Json(name: "Other.9.1.0.nupkg"),
            "otherRepo" => Json(url: "https://github.com/someone/ancosSantiye/releases/download/v9.1.0/AncosSantiye.9.1.0.nupkg"),
            "otherHost" => Json(url: "https://evil.example/ancoesat/ancosSantiye/releases/download/AncosSantiye.9.1.0.nupkg"),
            "noDigest" => Json(digest: ""),
            "md5" => Json(digest: "md5:" + new string('a', 32)),
            _ => Json(digest: "sha256:abc"),
        };

        Assert.Null(FromJson(json));
    }

    [Theory]
    [InlineData("v0.4.0", "0.4.0")]
    [InlineData("0.4.0+abc123", "0.4.0")]
    [InlineData("V1.2.3", "1.2.3")]
    public void Versions_AreRead(string text, string expected)
    {
        Assert.Equal(Version.Parse(expected), Releases.Parse(text));
    }

    [Theory]
    [InlineData("0.4")]
    [InlineData("0.4.0.1")]
    [InlineData("0.4.0-beta")]
    [InlineData("")]
    [InlineData(null)]
    public void OddVersions_AreNotVersions(string? text)
    {
        Assert.Null(Releases.Parse(text));
    }

    [Fact]
    public void TheRunningVersion_IsTheProjectVersion()
    {
        Assert.True(Releases.Current > new Version(0, 3, 1));
    }

    private string Package(string id = "AncosSantiye", string version = "9.1.0")
    {
        var path = Path.Combine(_dir, $"pkg-{Guid.NewGuid():N}.nupkg");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry($"{id}.nuspec");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write($"""<?xml version="1.0"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>{id}</id><version>{version}</version></metadata></package>""");
        }

        return path;
    }

    private static Release For(string path) =>
        new(new Version(9, 1, 0), "v9.1.0", "https://github.com/ancoesat/ancosSantiye/releases", Download, "AncosSantiye.9.1.0.nupkg",
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));

    [Fact]
    public void Check_AcceptsOnlyTheExactPackage()
    {
        var good = Package();
        Assert.Null(Updater.Check(good, For(good)));

        Assert.Contains("SHA-256", Updater.Check(good, For(good) with { Sha256 = Hash }));

        var other = Package(id: "SomethingElse");
        Assert.Contains("not an ancosSantiye package", Updater.Check(other, For(other)));

        var older = Package(version: "0.1.0");
        Assert.Contains("something else", Updater.Check(older, For(older)));
    }

    [Fact]
    public void Scripts_QuoteEveryPathAndArgument()
    {
        var plan = new UpdatePlan("/tmp/it's here", new Version(9, 1, 0), 4242, "/work/o'clock dir", ["--profile", "C:/my profiles/x.json"]);

        var sh = Updater.Script(plan, windows: false);
        Assert.Contains("--add-source '/tmp/it'\\''s here' --version 9.1.0", sh);
        Assert.Contains("cd '/work/o'\\''clock dir'", sh);
        Assert.Contains("nohup ancos-santiye '--profile' 'C:/my profiles/x.json'", sh);
        Assert.Contains("kill -0 4242", sh);

        var ps = Updater.Script(plan, windows: true);
        Assert.Contains("--add-source '/tmp/it''s here' --version 9.1.0", ps);
        Assert.Contains("Set-Location -LiteralPath '/work/o''clock dir'", ps);
        Assert.Contains("-ArgumentList @('--profile', '\"C:/my profiles/x.json\"')", ps);
        Assert.Contains("Wait-Process -Id 4242", ps);
    }

    [Fact]
    public void FromTheTerminal_TheScriptDoesNotRestart()
    {
        var plan = new UpdatePlan("/tmp/p", new Version(9, 1, 0), 1, "/w", Restart: null);

        Assert.DoesNotContain("nohup ancos-santiye", Updater.Script(plan, windows: false));
        Assert.DoesNotContain("Start-Process ancos-santiye", Updater.Script(plan, windows: true));
    }

    [Fact]
    public void TheShellScript_RunsAndInstalls()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // Stand-ins for dotnet and ancos-santiye that only write down how they were called.
        var bin = Path.Combine(_dir, "bin");
        Directory.CreateDirectory(bin);
        var calls = Path.Combine(_dir, "calls.txt");
        foreach (var name in new[] { "dotnet", "ancos-santiye" })
        {
            var fake = Path.Combine(bin, name);
            File.WriteAllText(fake, $"#!/bin/sh\necho \"{name} $*\" >> '{calls}'\n");
            File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var work = Path.Combine(_dir, "work dir");
        Directory.CreateDirectory(work);
        var script = Path.Combine(_dir, "update.sh");
        File.WriteAllText(script, Updater.Script(new UpdatePlan(Path.Combine(_dir, "pkg's"), new Version(9, 1, 0), 999999, work, ["--profile", "my profile.json"]), windows: false));

        var start = new ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true, UseShellExecute = false };
        start.ArgumentList.Add(script);
        start.Environment["PATH"] = bin + ":/usr/bin:/bin";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(10_000);
        for (var i = 0; i < 50 && (!File.Exists(calls) || File.ReadAllLines(calls).Length < 2); i++)
        {
            Thread.Sleep(100);
        }

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("is installed", output);
        var lines = File.ReadAllLines(calls);
        Assert.Equal($"dotnet tool update -g AncosSantiye --add-source {Path.Combine(_dir, "pkg's")} --version 9.1.0 --ignore-failed-sources", lines[0]);
        Assert.Equal("ancos-santiye --profile my profile.json", lines[1]);
    }
}
