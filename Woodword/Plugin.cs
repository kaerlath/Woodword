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

    private readonly WindowSystem windowSystem = new("Woodword");
    private readonly TranslationService translationService;
    private readonly MainWindow mainWindow;
    private readonly SettingsWindow settingsWindow;
    private bool disposed;

    internal Configuration Configuration { get; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.EnsureClientId();
        SaveConfiguration();

        translationService = new TranslationService();
        settingsWindow = new SettingsWindow(this);
        var backgroundPath = Path.Combine(
            PluginInterface.AssemblyLocation.Directory?.FullName ?? string.Empty,
            "Assets", "woodword-background-v2.png");
        var vinePath = Path.Combine(
            PluginInterface.AssemblyLocation.Directory?.FullName ?? string.Empty,
            "Assets", "vine-corner.png");
        var ravenPath = Path.Combine(
            PluginInterface.AssemblyLocation.Directory?.FullName ?? string.Empty,
            "Assets", "raven-header-v3.png");
        mainWindow = new MainWindow(
            this, translationService, settingsWindow, backgroundPath, vinePath, ravenPath);
        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(settingsWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open or close the Woodword translator.",
        });
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleSettingsUi;
    }

    internal void SaveConfiguration() => PluginInterface.SavePluginConfig(Configuration);
    private void OnCommand(string command, string arguments) => ToggleMainUi();
    private void ToggleMainUi() => mainWindow.Toggle();
    private void ToggleSettingsUi() => settingsWindow.Toggle();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleSettingsUi;
        CommandManager.RemoveHandler(CommandName);
        windowSystem.RemoveAllWindows();
        mainWindow.Dispose();
        settingsWindow.Dispose();
        translationService.Dispose();
    }
}
