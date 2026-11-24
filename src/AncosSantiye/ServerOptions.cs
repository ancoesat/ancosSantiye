namespace AncosSantiye;

/// <summary>Where the local server listens. Loopback only, never a public interface.</summary>
public static class ServerOptions
{
    public const int DefaultPort = 5170;

    public static string DefaultUrl => $"http://127.0.0.1:{DefaultPort}";
}
