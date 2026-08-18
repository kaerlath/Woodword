using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Woodword.Windows;

public sealed class SettingsWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private string relayToken;

    public SettingsWindow(Plugin plugin)
        : base("Woodword Settings##WoodwordSettings")
    {
        this.plugin = plugin;
        relayToken = plugin.Configuration.RelayToken;
        Size = new Vector2(460, 190);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 170),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        var hasBundledToken = !string.IsNullOrWhiteSpace(BuildInformation.BundledRelayToken);
        ImGui.TextWrapped(hasBundledToken
            ? "This release already carries Woodword's relay token. A token entered here overrides it for testing or rotation."
            : "This development build has no bundled relay token. Enter one here to speak through the relay.");
        ImGui.Spacing();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##RelayToken", ref relayToken, 512, ImGuiInputTextFlags.Password);
        ImGui.Spacing();
        if (ImGui.Button("Save token"))
        {
            plugin.Configuration.RelayToken = relayToken.Trim();
            plugin.SaveConfiguration();
            IsOpen = false;
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear")) relayToken = string.Empty;
        ImGui.Spacing();
        ImGui.TextDisabled($"Installation mark: {plugin.Configuration.ClientId}");
    }

    public void Dispose() => relayToken = string.Empty;
}
