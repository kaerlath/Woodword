using System.Reflection;

namespace Woodword;

internal static class BuildInformation
{
    private static readonly Version AssemblyVersion = typeof(BuildInformation).Assembly.GetName().Version
        ?? new Version(0, 0, 0);

    internal static string DisplayVersion { get; } =
        $"v{AssemblyVersion.Major}.{AssemblyVersion.Minor}.{Math.Max(0, AssemblyVersion.Build)}";

    internal static string BundledRelayToken { get; } =
        typeof(BuildInformation).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "WoodwordRelayToken")
            ?.Value ?? string.Empty;
}
