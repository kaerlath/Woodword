using System.Collections.Concurrent;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Woodword.Models;
using Woodword.Services;

namespace Woodword.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private static readonly Vector4 Moss = new(0.42f, 0.62f, 0.38f, 1f);
    private static readonly Vector4 PaleMoss = new(0.68f, 0.78f, 0.58f, 1f);
    private static readonly Vector4 DeepWood = new(0.045f, 0.065f, 0.052f, 0.94f);
    private static readonly Vector4 Gold = new(0.78f, 0.64f, 0.36f, 1f);

    private readonly Plugin plugin;
    private readonly TranslationService translationService;
    private readonly SettingsWindow settingsWindow;
    private readonly ConcurrentQueue<Action> uiUpdates = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private CancellationTokenSource? commonRequestCancellation;
    private CancellationTokenSource? vieranRequestCancellation;
    private string commonInput = string.Empty;
    private string vieranOutput = string.Empty;
    private string vieranInput = string.Empty;
    private string commonOutput = string.Empty;
    private string commonStatus = "The Wood listens.";
    private string vieranStatus = "The Wood listens.";
    private bool commonBusy;
    private bool vieranBusy;
    private bool commonInputActive;
    private bool vieranInputActive;
    private bool disposed;

    public MainWindow(Plugin plugin, TranslationService translationService, SettingsWindow settingsWindow)
        : base("Woodword##WoodwordMain")
    {
        this.plugin = plugin;
        this.translationService = translationService;
        this.settingsWindow = settingsWindow;
        Size = new Vector2(780, 690);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 520),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        while (uiUpdates.TryDequeue(out var update)) update();

        ImGui.TextColored(PaleMoss, "THE WOODWORD");
        ImGui.SameLine();
        ImGui.TextColored(Gold, "The Wood listens, and meaning takes root.");
        ImGui.SameLine(ImGui.GetWindowWidth() - 82);
        if (ImGui.Button("Settings")) settingsWindow.IsOpen = true;
        ImGui.Separator();
        ImGui.TextDisabled("THE TWO TONGUES");

        var available = ImGui.GetContentRegionAvail();
        var panelHeight = MathF.Max(210, (available.Y - ImGui.GetStyle().ItemSpacing.Y) / 2);
        DrawPanel("COMMON  \u2192  VIERAN", "Words offered in Common", "Rendered in the Vieran tongue",
            "Render into Vieran", "common", ref commonInput, ref vieranOutput,
            ref commonStatus, ref commonBusy, TranslationDirection.CommonToVieran, panelHeight, true);
        DrawPanel("VIERAN  \u2192  COMMON", "Words offered in Vieran", "Meaning returned in Common",
            "Translate into Common", "vieran", ref vieranInput, ref commonOutput,
            ref vieranStatus, ref vieranBusy, TranslationDirection.VieranToCommon, panelHeight, false);
    }

    private void DrawPanel(
        string heading, string inputLabel, string outputLabel, string actionLabel, string id,
        ref string input, ref string output, ref string status, ref bool busy,
        TranslationDirection direction, float height, bool showCopy)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, DeepWood);
        ImGui.PushStyleColor(ImGuiCol.Border, Moss);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);

        if (!ImGui.BeginChild($"{id}Panel", new Vector2(0, height), true))
        {
            ImGui.EndChild();
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);
            return;
        }

        ImGui.TextColored(PaleMoss, heading);
        ImGui.TextDisabled($"{inputLabel}  |  {input.Length}/{TranslationService.MaximumTextLength}");
        var boxHeight = MathF.Max(54, (height - 132) / 2);
        var inputWidth = MathF.Max(100, ImGui.GetContentRegionAvail().X - 12);
        ref var inputActive = ref direction == TranslationDirection.CommonToVieran
            ? ref commonInputActive
            : ref vieranInputActive;
        if (!inputActive) input = WrapForDisplay(input, inputWidth);
        ImGui.InputTextMultiline($"##{id}Input", ref input, TranslationService.MaximumTextLength + 1,
            new Vector2(-1, boxHeight));
        inputActive = ImGui.IsItemActive();
        if (!inputActive) input = WrapForDisplay(input, inputWidth);
        ImGui.TextDisabled(outputLabel);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.085f, 0.075f, 1f));
        if (ImGui.BeginChild($"{id}Output", new Vector2(-1, boxHeight), true))
            ImGui.TextWrapped(string.IsNullOrEmpty(output) ? "The Wood has not yet answered." : output);
        ImGui.EndChild();
        ImGui.PopStyleColor();

        ImGui.BeginDisabled(busy || string.IsNullOrWhiteSpace(input));
        if (ImGui.Button($"{actionLabel}##{id}")) StartTranslation(direction, NormalizeForTranslation(input));
        ImGui.EndDisabled();

        if (showCopy)
        {
            ImGui.SameLine();
            ImGui.BeginDisabled(string.IsNullOrEmpty(output));
            if (ImGui.Button($"Gather Vieran words##{id}"))
            {
                ImGui.SetClipboardText(output);
                status = "The rendered words have been gathered.";
            }
            ImGui.EndDisabled();
        }

        if (busy)
        {
            ImGui.SameLine();
            if (ImGui.Button($"Cancel##{id}")) Cancel(direction);
        }

        ImGui.SameLine();
        ImGui.TextColored(Gold, status);
        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    private void StartTranslation(TranslationDirection direction, string input)
    {
        if (direction == TranslationDirection.CommonToVieran)
        {
            commonRequestCancellation?.Cancel();
            commonRequestCancellation?.Dispose();
            commonRequestCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token);
            commonBusy = true;
            commonStatus = "The Wood is rendering your words...";
            _ = RunTranslationAsync(direction, input, commonRequestCancellation.Token);
        }
        else
        {
            vieranRequestCancellation?.Cancel();
            vieranRequestCancellation?.Dispose();
            vieranRequestCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token);
            vieranBusy = true;
            vieranStatus = "The Wood is listening for their meaning...";
            _ = RunTranslationAsync(direction, input, vieranRequestCancellation.Token);
        }
    }

    private async Task RunTranslationAsync(TranslationDirection direction, string input, CancellationToken token)
    {
        try
        {
            var result = await translationService.TranslateAsync(
                input, direction, GetRelayToken(), plugin.Configuration.ClientId, token);
            if (disposed || token.IsCancellationRequested) return;
            uiUpdates.Enqueue(() =>
            {
                if (direction == TranslationDirection.CommonToVieran)
                {
                    vieranOutput = result;
                    commonStatus = "The Wood has given the words a Vieran shape.";
                }
                else
                {
                    commonOutput = result;
                    vieranStatus = "The meaning returns in Common.";
                }
            });
        }
        catch (OperationCanceledException)
        {
            if (disposed) return;
            QueueStatus(direction, "The words were released before they took shape.");
        }
        catch (TranslationException ex)
        {
            QueueStatus(direction, ex.Message);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Unexpected translation failure");
            QueueStatus(direction, "An unfamiliar silence has fallen over the Wood.");
        }
        finally
        {
            if (!disposed)
            {
                uiUpdates.Enqueue(() =>
                {
                    if (direction == TranslationDirection.CommonToVieran) commonBusy = false;
                    else vieranBusy = false;
                });
            }
        }
    }

    private string GetRelayToken() => string.IsNullOrWhiteSpace(plugin.Configuration.RelayToken)
        ? BuildInformation.BundledRelayToken
        : plugin.Configuration.RelayToken;

    private static string NormalizeForTranslation(string text) => string.Join("\n\n",
        text.Replace("\r", string.Empty)
            .Split("\n\n", StringSplitOptions.None)
            .Select(paragraph => string.Join(' ', paragraph
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))));

    private static string WrapForDisplay(string text, float width)
    {
        var paragraphs = NormalizeForTranslation(text).Split("\n\n", StringSplitOptions.None);
        var wrapped = new List<string>(paragraphs.Length);
        foreach (var paragraph in paragraphs)
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var lines = new List<string>();
            var line = string.Empty;
            foreach (var word in words)
            {
                var candidate = line.Length == 0 ? word : $"{line} {word}";
                if (line.Length > 0 && ImGui.CalcTextSize(candidate).X > width)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                {
                    line = candidate;
                }
            }
            if (line.Length > 0) lines.Add(line);
            wrapped.Add(string.Join('\n', lines));
        }
        return string.Join("\n\n", wrapped);
    }

    private void QueueStatus(TranslationDirection direction, string value) => uiUpdates.Enqueue(() =>
    {
        if (direction == TranslationDirection.CommonToVieran) commonStatus = value;
        else vieranStatus = value;
    });

    private void Cancel(TranslationDirection direction)
    {
        if (direction == TranslationDirection.CommonToVieran) commonRequestCancellation?.Cancel();
        else vieranRequestCancellation?.Cancel();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetimeCancellation.Cancel();
        commonRequestCancellation?.Cancel();
        vieranRequestCancellation?.Cancel();
        commonRequestCancellation?.Dispose();
        vieranRequestCancellation?.Dispose();
        lifetimeCancellation.Dispose();
    }
}
