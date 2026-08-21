using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Textures;
using Woodword.Services;
using Woodword.Windows;

namespace Woodword;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/woodword";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("Woodword");
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly TranslationService translationService;
    private readonly TranslationHistoryService historyService;
    private readonly LiveChatTranslationService liveChatService;
    private readonly MainWindow mainWindow;
    private readonly SettingsWindow settingsWindow;
    private readonly HistoryWindow historyWindow;
    private bool disposed;

    internal Configuration Configuration { get; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.EnsureClientId();
        Configuration.EnsureValidHistoryLimit();
        SaveConfiguration();

        translationService = new TranslationService();
        liveChatService = new LiveChatTranslationService(
            ChatGui, translationService, GetRelayToken, () => Configuration.ClientId,
            () => Configuration.LiveAccessCode);
        historyService = new TranslationHistoryService(Path.Combine(
            PluginInterface.GetPluginConfigDirectory(), "translation-history.jsonl"));
        historyWindow = new HistoryWindow(historyService);
        settingsWindow = new SettingsWindow(this);
        var backgroundPath = Path.Combine(
            PluginInterface.AssemblyLocation.Directory?.FullName ?? string.Empty,
            "Assets", "woodword-background-v2.png");
        var panelTopPath = Path.Combine(
            PluginInterface.AssemblyLocation.Directory?.FullName ?? string.Empty,
            "Assets", "panel-top-botanical.png");
        var panelBottomPath = Path.Combine(
            PluginInterface.AssemblyLocation.Directory?.FullName ?? string.Empty,
            "Assets", "panel-bottom-botanical.png");
        var ravenPath = Path.Combine(
            PluginInterface.AssemblyLocation.Directory?.FullName ?? string.Empty,
            "Assets", "raven-header-v3.png");
        mainWindow = new MainWindow(
            this, translationService, historyService, liveChatService, settingsWindow, backgroundPath,
            panelTopPath, panelBottomPath, ravenPath);
        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(settingsWindow);
        windowSystem.AddWindow(historyWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open or close the Woodword translator.",
        });
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleSettingsUi;
        if (Configuration.LiveChatTranslationEnabled && !string.IsNullOrWhiteSpace(Configuration.LiveAccessCode))
            _ = ValidateAndSetLiveChatListeningAsync(true, Configuration.LiveAccessCode);
    }

    internal void SaveConfiguration() => PluginInterface.SavePluginConfig(Configuration);
    internal void OpenHistory() => historyWindow.OpenAndRefresh();
    internal string GetRelayToken() => string.IsNullOrWhiteSpace(Configuration.RelayToken)
        ? BuildInformation.BundledRelayToken
        : Configuration.RelayToken;
    internal bool IsLiveChatListening => liveChatService.IsEnabled;
    internal void DisableLiveChatListening()
    {
        Configuration.LiveChatTranslationEnabled = false;
        SaveConfiguration();
        liveChatService.SetEnabled(false);
    }
    internal async Task<string> ValidateAndSetLiveChatListeningAsync(bool enabled, string code)
    {
        if (!enabled)
        {
            DisableLiveChatListening();
            return "Live listening is paused.";
        }

        var trimmedCode = code.Trim();
        await translationService.ValidateLiveAccessAsync(
            GetRelayToken(), Configuration.ClientId, trimmedCode, lifetimeCancellation.Token);
        Configuration.LiveAccessCode = trimmedCode;
        Configuration.LiveChatTranslationEnabled = true;
        SaveConfiguration();
        liveChatService.SetEnabled(true);
        return "The validation code was accepted. The Wood may now listen.";
    }
    internal async Task EnforceHistoryLimitAsync()
    {
        try
        {
            await historyService.EnforceLimitAsync(Configuration.HistoryMaxMegabytes);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not apply Woodword's translation history limit");
        }
    }
    private void OnCommand(string command, string arguments) => ToggleMainUi();
    private void ToggleMainUi() => mainWindow.Toggle();
    private void ToggleSettingsUi() => settingsWindow.Toggle();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetimeCancellation.Cancel();
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleSettingsUi;
        CommandManager.RemoveHandler(CommandName);
        windowSystem.RemoveAllWindows();
        mainWindow.Dispose();
        settingsWindow.Dispose();
        historyWindow.Dispose();
        liveChatService.Dispose();
        historyService.Dispose();
        translationService.Dispose();
        lifetimeCancellation.Dispose();
    }
}
