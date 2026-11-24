using AncosSantiye.Adapters;
using AncosSantiye.Events;

namespace AncosSantiye.Tests;

public class DeliveryFileTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 24, 20, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("kasa-api-009-gemini.md", "kasa-api", "kasa-api-009", "gemini")]
    [InlineData("kasa-api-1234-studio.md", "kasa-api", "kasa-api-1234", "studio")]
    [InlineData("demo-000-gemini.md", "demo", "demo-000", "gemini")]
    [InlineData("KASA-API-009-GEMINI.MD", "KASA-API", "KASA-API-009", "gemini")]
    public void TryParse_ReadsAValidName(string fileName, string project, string piece, string lane)
    {
        var ok = DeliveryFile.TryParse(fileName, out var info);

        Assert.True(ok);
        Assert.Equal(project, info!.Project);
        Assert.Equal(piece, info.PieceId);
        Assert.Equal(lane, info.Lane);
    }

    [Theory]
    [InlineData("")]
    [InlineData("kasa-api-09-gemini.md")]
    [InlineData("kasa-api-gemini.md")]
    [InlineData("kasa-api-009-opus.md")]
    [InlineData("kasa-api-009-gemini.diff")]
    [InlineData("kasa-api-009-gemini.md.bak")]
    [InlineData("kasa-api-009-gemini-check.log")]
    [InlineData("kasa-api-009-gemini.md ")]
    [InlineData("-009-gemini.md")]
    [InlineData("C:/models/kasa-api-009-gemini.md")]
    [InlineData("models\\kasa-api-009-gemini.md")]
    public void TryParse_RejectsEverythingElse(string fileName)
    {
        var ok = DeliveryFile.TryParse(fileName, out var info);

        Assert.False(ok);
        Assert.Null(info);
    }

    [Fact]
    public void ToEvent_BuildsTheGeminiDelivery()
    {
        Assert.True(DeliveryFile.TryParse("kasa-api-009-gemini.md", out var info));

        var e = DeliveryFile.ToEvent(info!, At);

        Assert.Equal(EventKinds.DeliveryArrived, e.Kind);
        Assert.Equal(Sources.Folder, e.Source);
        Assert.Equal("kasa-api", e.Project);
        Assert.Equal("kasa-api-009", e.Piece);
        Assert.Equal("supplier-gemini", e.Actor.Id);
        Assert.Equal(Roles.Supplier, e.Actor.Role);
        Assert.Equal("Gemini", e.Actor.Name);
        Assert.Equal(At, e.Ts);
        Assert.Null(e.Data);
    }

    [Fact]
    public void ToEvent_NamesTheStudioActor()
    {
        Assert.True(DeliveryFile.TryParse("kasa-api-010-studio.md", out var info));

        var e = DeliveryFile.ToEvent(info!, At);

        Assert.Equal("supplier-studio", e.Actor.Id);
        Assert.Equal("AI Studio", e.Actor.Name);
        Assert.Equal("kasa-api-010", e.Piece);
    }
}
