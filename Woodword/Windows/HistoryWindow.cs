using System.Collections.Concurrent;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Woodword.Models;
using Woodword.Services;

namespace Woodword.Windows;

public sealed class HistoryWindow : Window, IDisposable
{
    private const int PageSize = 100;
    private readonly TranslationHistoryService historyService;
    private readonly ConcurrentQueue<Action> uiUpdates = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly List<HistoryEntry> entries = [];
    private long? olderEntriesBeforeOffset;
    private bool loading;
    private string error = string.Empty;
    private bool disposed;

    public HistoryWindow(TranslationHistoryService historyService)
        : base("Woodword Translation History##WoodwordHistory")
    {
        this.historyService = historyService;
        Size = new Vector2(680, 620);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 360),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void OpenAndRefresh()
    {
        IsOpen = true;
        StartLoad(true);
    }

    public override void Draw()
    {
        while (uiUpdates.TryDequeue(out var update)) update();

        ImGui.TextWrapped("Translation history is stored only on this computer. Newest entries appear first.");
        if (ImGui.Button("Refresh")) StartLoad(true);
        ImGui.SameLine();
        ImGui.BeginDisabled(loading || olderEntriesBeforeOffset is null);
        if (ImGui.Button("Load older entries")) StartLoad(false);
        ImGui.EndDisabled();
        if (loading)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("Listening for older echoes...");
        }

        if (!string.IsNullOrEmpty(error))
            ImGui.TextColored(new Vector4(0.9f, 0.45f, 0.4f, 1f), error);

        ImGui.Separator();
        if (!ImGui.BeginChild("HistoryEntries", new Vector2(0, 0), false))
        {
            ImGui.EndChild();
            return;
        }

        if (!loading && entries.Count == 0)
            ImGui.TextDisabled("No translated words have yet been recorded.");

        foreach (var entry in entries)
        {
            var commonToVieran = entry.Direction == TranslationDirection.CommonToVieran;
            ImGui.TextColored(
                new Vector4(0.68f, 0.78f, 0.58f, 1f),
                commonToVieran ? "COMMON  →  VIERAN" : "VIERAN  →  COMMON");
            ImGui.SameLine();
            ImGui.TextDisabled(entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd  h:mm:ss tt"));
            ImGui.TextDisabled(commonToVieran ? "Common offered:" : "Vieran offered:");
            ImGui.TextWrapped(entry.Input);
            ImGui.Spacing();
            ImGui.TextDisabled(commonToVieran ? "Vieran rendering:" : "Common meaning:");
            ImGui.TextWrapped(entry.Output);
            ImGui.Separator();
        }

        ImGui.EndChild();
    }

    private void StartLoad(bool reset)
    {
        if (loading || disposed) return;
        loading = true;
        error = string.Empty;
        var beforeOffset = reset ? null : olderEntriesBeforeOffset;
        _ = LoadAsync(reset, beforeOffset, lifetimeCancellation.Token);
    }

    private async Task LoadAsync(bool reset, long? beforeOffset, CancellationToken cancellationToken)
    {
        try
        {
            var page = await historyService.ReadPageAsync(beforeOffset, PageSize, cancellationToken);
            if (disposed) return;
            uiUpdates.Enqueue(() =>
            {
                if (reset) entries.Clear();
                entries.AddRange(page.Entries);
                olderEntriesBeforeOffset = page.OlderEntriesBeforeOffset;
                loading = false;
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not read Woodword translation history");
            if (!disposed)
                uiUpdates.Enqueue(() =>
                {
                    error = "The Wood could not recall its recorded echoes.";
                    loading = false;
                });
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetimeCancellation.Cancel();
        lifetimeCancellation.Dispose();
    }
}
