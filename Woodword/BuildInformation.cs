using System.Reflection;

namespace Woodword;

internal static class BuildInformation
{
    internal static string BundledRelayToken { get; } =
        typeof(BuildInformation).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "WoodwordRelayToken")
            ?.Value ?? string.Empty;
}
