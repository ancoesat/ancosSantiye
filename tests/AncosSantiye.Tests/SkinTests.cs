using System.Buffers.Binary;
using System.Text.Json;

namespace AncosSantiye.Tests;

/// <summary>Every skin.json must point at files that exist and frame sizes that fit those files.</summary>
public class SkinTests
{
    private static readonly string Skins = Path.Combine(AppContext.BaseDirectory, "skins");

    public static TheoryData<string> AllSkins()
    {
        var data = new TheoryData<string>();
        foreach (var dir in Directory.GetDirectories(Skins))
        {
            data.Add(Path.GetFileName(dir));
        }

        return data;
    }

    private static JsonElement Load(string skin) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Skins, skin, "skin.json"))).RootElement;

    private static (int Width, int Height) PngSize(string path)
    {
        var header = new byte[24];
        using (var file = File.OpenRead(path))
        {
            file.ReadExactly(header);
        }

        Assert.True(header[1] == 'P' && header[2] == 'N' && header[3] == 'G', $"{path} is not a PNG");
        return (BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20)));
    }

    [Theory]
    [MemberData(nameof(AllSkins))]
    public void EveryCharacterSheet_ExistsAndHoldsWholeFrames(string skin)
    {
        foreach (var character in Load(skin).GetProperty("characters").EnumerateObject())
        {
            Assert.True(character.Value.TryGetProperty("idle", out _), $"{character.Name} has no idle animation");
            foreach (var anim in character.Value.EnumerateObject())
            {
                var path = Path.Combine(Skins, skin, anim.Value.GetProperty("sheet").GetString()!);
                Assert.True(File.Exists(path), $"missing {path}");

                var (width, height) = PngSize(path);
                var frameW = anim.Value.GetProperty("frameW").GetInt32();
                Assert.True(width % frameW == 0, $"{character.Name}/{anim.Name}: width {width} is not a multiple of {frameW}");
                Assert.Equal(anim.Value.GetProperty("frameH").GetInt32(), height);
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllSkins))]
    public void EveryTile_FitsInsideItsSheet(string skin)
    {
        var def = Load(skin);
        Assert.True(File.Exists(Path.Combine(Skins, skin, def.GetProperty("background").GetString()!)));

        foreach (var tile in def.GetProperty("tiles").EnumerateObject())
        {
            var path = Path.Combine(Skins, skin, tile.Value.GetProperty("sheet").GetString()!);
            var (width, height) = PngSize(path);
            int Get(string name) => tile.Value.GetProperty(name).GetInt32();

            Assert.True(Get("x") + Get("w") <= width && Get("y") + Get("h") <= height, $"tile {tile.Name} is outside {path}");
        }
    }

    [Theory]
    [MemberData(nameof(AllSkins))]
    public void EveryRole_HasCharactersThatExist(string skin)
    {
        var def = Load(skin);
        var characters = def.GetProperty("characters").EnumerateObject().Select(c => c.Name).ToHashSet();

        foreach (var role in new[] { "chief", "worker", "subcontractor", "inspector", "architect", "supplier" })
        {
            var cast = def.GetProperty("roles").GetProperty(role).EnumerateArray().Select(c => c.GetString()!).ToList();
            Assert.NotEmpty(cast);
            Assert.All(cast, c => Assert.Contains(c, characters));
        }
    }
}
