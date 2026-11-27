using System.Runtime.CompilerServices;

namespace AncosSantiye.Tests;

internal static class TestSetup
{
    /// <summary>Servers built in tests never ask GitHub for updates: tests stay off the network.</summary>
    [ModuleInitializer]
    internal static void NoUpdateChecks() => Environment.SetEnvironmentVariable("Santiye__UpdateCheck", "false");
}
