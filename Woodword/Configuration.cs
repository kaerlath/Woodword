using Dalamud.Configuration;

namespace Woodword;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public string ClientId { get; set; } = string.Empty;
    public string RelayToken { get; set; } = string.Empty;

    public void EnsureClientId()
    {
        if (!Guid.TryParse(ClientId, out _))
            ClientId = Guid.NewGuid().ToString("D");
    }
}
