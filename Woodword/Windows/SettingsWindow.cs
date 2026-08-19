using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Woodword.Windows;

public sealed class SettingsWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private string relayToken;
    private int historyMaxMegabytes;

    public SettingsWindow(Plugin plugin)
        : base("Woodword Settings##WoodwordSettings")
    {
        this.plugin = plugin;
        relayToken = plugin.Configuration.RelayToken;
        historyMaxMegabytes = plugin.Configuration.HistoryMaxMegabytes;
        Size = new Vector2(500, 300);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(440, 260),
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
        if (ImGui.Button("Save settings"))
        {
            plugin.Configuration.RelayToken = relayToken.Trim();
            plugin.Configuration.HistoryMaxMegabytes = Math.Clamp(historyMaxMegabytes, 1, 1024);
            plugin.SaveConfiguration();
            _ = plugin.EnforceHistoryLimitAsync();
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear")) relayToken = string.Empty;
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Translation history");
        ImGui.TextWrapped("Successful translations are recorded only on this computer. Oldest entries are removed as the selected storage limit is reached.");
        ImGui.SetNextItemWidth(260f);
        ImGui.SliderInt("Maximum history size (MB)", ref historyMaxMegabytes, 1, 1024);
        if (ImGui.Button("Open translation logs")) plugin.OpenHistory();
        ImGui.Spacing();
        ImGui.TextDisabled($"Installation mark: {plugin.Configuration.ClientId}");
    }

    public void Dispose() => relayToken = string.Empty;
}
