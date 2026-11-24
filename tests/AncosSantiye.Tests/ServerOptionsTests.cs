namespace AncosSantiye.Tests;

public class ServerOptionsTests
{
    [Fact]
    public void DefaultUrl_IsLoopbackOnly()
    {
        var uri = new Uri(ServerOptions.DefaultUrl);

        Assert.True(uri.IsLoopback);
        Assert.Equal("127.0.0.1", uri.Host);
    }
}
