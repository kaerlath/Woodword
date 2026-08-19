using Dalamud.Configuration;

namespace Woodword;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;
    public string ClientId { get; set; } = string.Empty;
    public string RelayToken { get; set; } = string.Empty;
    public int HistoryMaxMegabytes { get; set; } = 50;

    public void EnsureClientId()
    {
        if (!Guid.TryParse(ClientId, out _))
            ClientId = Guid.NewGuid().ToString("D");
    }

    public void EnsureValidHistoryLimit() => HistoryMaxMegabytes = Math.Clamp(HistoryMaxMegabytes, 1, 1024);
}
