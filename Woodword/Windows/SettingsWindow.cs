using System.Numerics;
using System.Collections.Concurrent;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Woodword.Services;

namespace Woodword.Windows;

public sealed class SettingsWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private string relayToken;
    private int historyMaxMegabytes;
    private bool liveChatTranslationEnabled;
    private string liveAccessCode;
    private string liveAccessStatus = "A validation code is required to unlock live listening.";
    private bool validatingLiveAccess;
    private readonly ConcurrentQueue<Action> uiUpdates = new();

    public SettingsWindow(Plugin plugin)
        : base("Woodword Settings##WoodwordSettings")
    {
        this.plugin = plugin;
        relayToken = plugin.Configuration.RelayToken;
        historyMaxMegabytes = plugin.Configuration.HistoryMaxMegabytes;
        liveChatTranslationEnabled = plugin.Configuration.LiveChatTranslationEnabled;
        liveAccessCode = plugin.Configuration.LiveAccessCode;
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
        while (uiUpdates.TryDequeue(out var update)) update();
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
        ImGui.Separator();
        ImGui.Text("Live Vieran listening");
        ImGui.TextWrapped("This optional feature requires a relay validation code issued by Woodword's author.");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##LiveAccessCode", ref liveAccessCode, 256, ImGuiInputTextFlags.Password);
        ImGui.BeginDisabled(validatingLiveAccess || string.IsNullOrWhiteSpace(liveAccessCode));
        if (ImGui.Button("Validate code and begin listening")) StartLiveValidation();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!plugin.IsLiveChatListening);
        if (ImGui.Button("Pause listening"))
        {
            plugin.DisableLiveChatListening();
            liveChatTranslationEnabled = false;
            liveAccessStatus = "Live listening is paused. The saved code remains available.";
        }
        ImGui.EndDisabled();
        ImGui.TextColored(
            plugin.IsLiveChatListening ? new Vector4(0.68f, 0.78f, 0.58f, 1f) : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled],
            validatingLiveAccess ? "The Wood is examining the validation code..." : liveAccessStatus);
        ImGui.TextWrapped("Off by default. While enabled, only messages that Woodword locally identifies as likely Icelandic are sent to the relay. Other channels and messages remain on this computer. Woodword never changes or posts chat.");
        ImGui.Spacing();
        ImGui.TextDisabled($"Installation mark: {plugin.Configuration.ClientId}");
    }

    public override void OnOpen()
    {
        relayToken = plugin.Configuration.RelayToken;
        historyMaxMegabytes = plugin.Configuration.HistoryMaxMegabytes;
        liveChatTranslationEnabled = plugin.Configuration.LiveChatTranslationEnabled;
        liveAccessCode = plugin.Configuration.LiveAccessCode;
        liveAccessStatus = plugin.IsLiveChatListening
            ? "The code is authorized and the Wood is listening."
            : "A validation code is required to unlock live listening.";
    }

    private void StartLiveValidation()
    {
        validatingLiveAccess = true;
        var code = liveAccessCode;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await plugin.ValidateAndSetLiveChatListeningAsync(true, code);
                uiUpdates.Enqueue(() =>
                {
                    validatingLiveAccess = false;
                    liveChatTranslationEnabled = true;
                    liveAccessStatus = result;
                });
            }
            catch (TranslationException ex)
            {
                uiUpdates.Enqueue(() =>
                {
                    validatingLiveAccess = false;
                    liveChatTranslationEnabled = false;
                    liveAccessStatus = ex.Message;
                });
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Could not validate Woodword live access");
                uiUpdates.Enqueue(() =>
                {
                    validatingLiveAccess = false;
                    liveChatTranslationEnabled = false;
                    liveAccessStatus = "The Wood could not examine that code.";
                });
            }
        });
    }

    public void Dispose()
    {
        relayToken = string.Empty;
        liveAccessCode = string.Empty;
    }
}
