using System.Collections.Concurrent;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Woodword.Models;
using Woodword.Services;

namespace Woodword.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private static readonly Vector4 Moss = new(0.42f, 0.62f, 0.38f, 1f);
    private static readonly Vector4 PaleMoss = new(0.68f, 0.78f, 0.58f, 1f);
    private static readonly Vector4 DeepWood = new(0.035f, 0.052f, 0.04f, 0.72f);
    private static readonly Vector4 Gold = new(0.78f, 0.64f, 0.36f, 1f);
    private static readonly Vector4 ButtonGreen = new(0.17f, 0.29f, 0.17f, 1f);
    private static readonly Vector4 ButtonHover = new(0.25f, 0.4f, 0.23f, 1f);
    private const float PanelGutter = 46f;
    private const float BotanicalOutputLabelInset = 52f;

    private readonly Plugin plugin;
    private readonly TranslationService translationService;
    private readonly TranslationHistoryService historyService;
    private readonly LiveChatTranslationService liveChatService;
    private readonly SettingsWindow settingsWindow;
    private readonly string backgroundPath;
    private readonly string panelTopPath;
    private readonly string panelBottomPath;
    private readonly string ravenPath;
    private readonly ConcurrentQueue<Action> uiUpdates = new();
    private readonly List<LiveTranslationEntry> liveTranslations = new();
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
    private string liveStatus = "Live listening is paused.";
    private bool disposed;

    public MainWindow(
        Plugin plugin,
        TranslationService translationService,
        TranslationHistoryService historyService,
        LiveChatTranslationService liveChatService,
        SettingsWindow settingsWindow,
        string backgroundPath,
        string panelTopPath,
        string panelBottomPath,
        string ravenPath)
        : base("Woodword##WoodwordMain")
    {
        this.plugin = plugin;
        this.translationService = translationService;
        this.historyService = historyService;
        this.liveChatService = liveChatService;
        this.settingsWindow = settingsWindow;
        this.backgroundPath = backgroundPath;
        this.panelTopPath = panelTopPath;
        this.panelBottomPath = panelBottomPath;
        this.ravenPath = ravenPath;
        liveChatService.TranslationReceived += OnLiveTranslationReceived;
        liveChatService.StatusChanged += OnLiveStatusChanged;
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

        DrawBackground();
        PushWoodwordControls();

        DrawRavenHeaderIcon();
        ImGui.SameLine();
        ImGui.TextColored(Gold, "WOODWORD");
        ImGui.SameLine();
        ImGui.TextColored(PaleMoss, "  The Wood listens, and meaning takes root.");
        ImGui.SameLine(ImGui.GetWindowWidth() - 137);
        ImGui.TextDisabled(BuildInformation.DisplayVersion);
        ImGui.SameLine();
        if (ImGui.Button("Settings")) settingsWindow.IsOpen = true;
        DrawOrnamentalRule();

        if (ImGui.BeginTabBar("##WoodwordTabs"))
        {
            if (ImGui.BeginTabItem("Translator"))
            {
                var available = ImGui.GetContentRegionAvail();
                var panelHeight = MathF.Max(210, (available.Y - ImGui.GetStyle().ItemSpacing.Y) / 2);
                DrawPanel("COMMON  \u2192  VIERAN", "Words offered in Common", "Common rendered in the Vieran tongue",
                    "Render into Vieran", "common", ref commonInput, ref vieranOutput,
                    ref commonStatus, ref commonBusy, TranslationDirection.CommonToVieran, panelHeight, true);
                DrawPanel("VIERAN  \u2192  COMMON", "Words offered in Vieran", "Vieran meaning returned in Common",
                    "Translate into Common", "vieran", ref vieranInput, ref commonOutput,
                    ref vieranStatus, ref vieranBusy, TranslationDirection.VieranToCommon, panelHeight, false);
                ImGui.EndTabItem();
            }

            var liveLabel = liveChatService.IsEnabled ? "Live Vieran  \u25cf" : "Live Vieran";
            if (ImGui.BeginTabItem(liveLabel))
            {
                DrawLiveTranslations();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        PopWoodwordControls();
    }

    private void DrawLiveTranslations()
    {
        var listening = liveChatService.IsEnabled;
        ImGui.TextColored(listening ? PaleMoss : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled],
            listening ? "LISTENING" : "PAUSED");
        ImGui.SameLine();
        ImGui.TextWrapped(liveStatus);

        if (ImGui.Button(listening ? "Pause listening" : "Unlock in Settings"))
        {
            if (listening) plugin.DisableLiveChatListening();
            else settingsWindow.IsOpen = true;
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(liveTranslations.Count == 0);
        if (ImGui.Button("Clear live feed")) liveTranslations.Clear();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextDisabled("Say and custom emotes only  |  current session  |  newest 100");
        ImGui.Separator();

        ImGui.PushStyleColor(ImGuiCol.ChildBg, DeepWood);
        if (ImGui.BeginChild("##LiveTranslationFeed", new Vector2(0, 0), true))
        {
            if (liveTranslations.Count == 0)
            {
                ImGui.TextDisabled(listening
                    ? "The Wood has not yet heard Vieran words nearby."
                    : "Begin listening when you wish the Wood to hear nearby Vieran words.");
            }
            else
            {
                foreach (var entry in liveTranslations)
                {
                    ImGui.TextColored(Gold, $"{entry.Timestamp:t}  {entry.Channel}");
                    if (!string.IsNullOrWhiteSpace(entry.Sender))
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(PaleMoss, entry.Sender);
                    }
                    ImGui.TextWrapped(entry.VieranText);
                    ImGui.Indent(14f);
                    ImGui.TextColored(PaleMoss, "Common meaning");
                    ImGui.TextWrapped(entry.CommonText);
                    ImGui.Unindent(14f);
                    ImGui.Separator();
                }
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void OnLiveTranslationReceived(LiveTranslationEntry entry) => uiUpdates.Enqueue(() =>
    {
        liveTranslations.Insert(0, entry);
        if (liveTranslations.Count > 100)
            liveTranslations.RemoveRange(100, liveTranslations.Count - 100);
    });

    private void OnLiveStatusChanged(string status) => uiUpdates.Enqueue(() => liveStatus = status);

    private void DrawRavenHeaderIcon()
    {
        const float iconSize = 30f;
        var texture = Plugin.TextureProvider.GetFromFile(ravenPath).GetWrapOrDefault();
        if (texture is not null)
        {
            var position = ImGui.GetCursorScreenPos() + new Vector2(0, -7);
            var drawList = ImGui.GetWindowDrawList();
            var center = position + new Vector2(iconSize / 2f);

            // A pale moon disc keeps the blue-black raven legible against the forest.
            drawList.AddCircleFilled(
                center,
                iconSize / 2f,
                ImGui.GetColorU32(new Vector4(0.62f, 0.69f, 0.72f, 0.94f)),
                32);
            drawList.AddCircle(
                center,
                iconSize / 2f,
                ImGui.GetColorU32(new Vector4(0.72f, 0.61f, 0.27f, 0.95f)),
                32,
                1.25f);
            drawList.AddImage(
                texture.Handle,
                position + new Vector2(1f),
                position + new Vector2(iconSize - 1f),
                Vector2.Zero,
                Vector2.One,
                ImGui.GetColorU32(Vector4.One));
        }
        ImGui.Dummy(new Vector2(iconSize + 2, ImGui.GetTextLineHeight()));
    }

    private void DrawBackground()
    {
        var texture = Plugin.TextureProvider.GetFromFile(backgroundPath).GetWrapOrDefault();
        if (texture is null) return;

        var drawList = ImGui.GetWindowDrawList();
        var position = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        drawList.AddImage(
            texture.Handle,
            position,
            position + size,
            Vector2.Zero,
            Vector2.One,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.72f)));
        drawList.AddRectFilled(
            position,
            position + size,
            ImGui.GetColorU32(new Vector4(0.015f, 0.025f, 0.018f, 0.16f)));
    }

    private static void DrawOrnamentalRule()
    {
        var drawList = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var end = start + new Vector2(ImGui.GetContentRegionAvail().X, 0);
        var center = (start + end) / 2;
        var color = ImGui.GetColorU32(new Vector4(Gold.X, Gold.Y, Gold.Z, 0.6f));
        drawList.AddLine(start, center - new Vector2(8, 0), color, 1f);
        drawList.AddLine(center + new Vector2(8, 0), end, color, 1f);
        drawList.AddQuadFilled(
            center + new Vector2(0, -4),
            center + new Vector2(4, 0),
            center + new Vector2(0, 4),
            center + new Vector2(-4, 0),
            color);
        ImGui.Dummy(new Vector2(0, 8));
    }

    private static void PushWoodwordControls()
    {
        ImGui.PushStyleColor(ImGuiCol.Button, ButtonGreen);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ButtonHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Moss);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.055f, 0.07f, 0.055f, 0.94f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.075f, 0.1f, 0.07f, 0.98f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.09f, 0.12f, 0.08f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
    }

    private static void PopWoodwordControls()
    {
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(6);
    }

    private void DrawPanel(
        string heading, string inputLabel, string outputLabel, string actionLabel, string id,
        ref string input, ref string output, ref string status, ref bool busy,
        TranslationDirection direction, float height, bool showCopy)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, DeepWood);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(Moss.X, Moss.Y, Moss.Z, 0.58f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);

        var panelPosition = ImGui.GetCursorScreenPos();
        var panelSize = new Vector2(ImGui.GetContentRegionAvail().X, height);

        if (!ImGui.BeginChild($"{id}Panel", new Vector2(0, height), true))
        {
            ImGui.EndChild();
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);
            return;
        }

        DrawPanelBotanicals(panelPosition, panelSize);
        ImGui.Indent(PanelGutter);
        DrawRightAlignedHeader(heading, PaleMoss);
        DrawRightAlignedHeader(
            $"{inputLabel}  |  {UnwrapDisplayText(input).Length}/{TranslationService.MaximumTextLength}",
            ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        var boxHeight = MathF.Max(54, (height - 132) / 2);
        var inputWidth = MathF.Max(100, ImGui.GetContentRegionAvail().X - PanelGutter - 12);
        ref var inputActive = ref direction == TranslationDirection.CommonToVieran
            ? ref commonInputActive
            : ref vieranInputActive;
        if (!inputActive) input = WrapForDisplay(input, inputWidth);
        ImGui.InputTextMultiline($"##{id}Input", ref input, TranslationService.MaximumTextLength + 512,
            new Vector2(-PanelGutter, boxHeight), ImGuiInputTextFlags.CallbackEdit,
            data => WrapDuringEdit(data, inputWidth));
        inputActive = ImGui.IsItemActive();
        var fieldMin = ImGui.GetItemRectMin();
        var fieldMax = ImGui.GetItemRectMax();
        var fieldDrawList = ImGui.GetWindowDrawList();
        if (string.IsNullOrWhiteSpace(UnwrapDisplayText(input)))
        {
            fieldDrawList.AddText(
                fieldMin + new Vector2(12f, 10f),
                ImGui.GetColorU32(new Vector4(0.68f, 0.76f, 0.64f, inputActive ? 0.72f : 0.55f)),
                "What do you wish to say?");
        }
        if (inputActive)
        {
            fieldDrawList.AddRect(
                fieldMin - new Vector2(2f), fieldMax + new Vector2(2f),
                ImGui.GetColorU32(new Vector4(0.27f, 0.58f, 0.57f, 0.2f)),
                5f, ImDrawFlags.None, 3f);
            fieldDrawList.AddRect(
                fieldMin - Vector2.One, fieldMax + Vector2.One,
                ImGui.GetColorU32(new Vector4(0.38f, 0.76f, 0.72f, 0.92f)),
                4f, ImDrawFlags.None, 1.25f);
        }
        if (!inputActive)
        {
            input = WrapForDisplay(input, inputWidth);
        }
        ImGui.Indent(BotanicalOutputLabelInset);
        ImGui.TextDisabled(outputLabel);
        ImGui.Unindent(BotanicalOutputLabelInset);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.085f, 0.075f, 1f));
        if (ImGui.BeginChild($"{id}Output", new Vector2(-PanelGutter, boxHeight), true))
            ImGui.TextWrapped(string.IsNullOrEmpty(output) ? "The Wood has not yet answered." : output);
        ImGui.EndChild();
        ImGui.PopStyleColor();

        ImGui.BeginDisabled(busy || string.IsNullOrWhiteSpace(input));
        if (ImGui.Button($"{actionLabel}##{id}")) StartTranslation(direction, NormalizeForTranslation(input));
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(busy || string.IsNullOrEmpty(input));
        if (ImGui.Button($"Clear##{id}"))
        {
            input = string.Empty;
            inputActive = false;
            status = "The offered words have been released.";
        }
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
        ImGui.Unindent(PanelGutter);
        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    private void DrawPanelBotanicals(Vector2 position, Vector2 size)
    {
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRect(
            position + new Vector2(7f),
            position + size - new Vector2(7f),
            ImGui.GetColorU32(new Vector4(0.48f, 0.62f, 0.48f, 0.48f)),
            5f,
            ImDrawFlags.None,
            1.4f);

        var topTexture = Plugin.TextureProvider.GetFromFile(panelTopPath).GetWrapOrDefault();
        if (topTexture is not null)
        {
            var topWidth = MathF.Min(390f, size.X * 0.58f);
            var topSize = new Vector2(topWidth, topWidth * 0.5625f);
            drawList.AddImage(topTexture.Handle, position, position + topSize,
                Vector2.Zero, Vector2.One,
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.66f)));
        }

        var bottomTexture = Plugin.TextureProvider.GetFromFile(panelBottomPath).GetWrapOrDefault();
        if (bottomTexture is not null)
        {
            var bottomWidth = MathF.Min(315f, size.X * 0.46f);
            var bottomSize = new Vector2(bottomWidth, bottomWidth * 0.5625f);
            var bottomPosition = position + new Vector2(size.X - bottomSize.X, size.Y - bottomSize.Y);
            drawList.AddImage(bottomTexture.Handle, bottomPosition, bottomPosition + bottomSize,
                new Vector2(1f, 0f), new Vector2(0f, 1f),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.78f)));
        }
    }

    private static void DrawRightAlignedHeader(string text, Vector4 color)
    {
        var rightEdge = ImGui.GetWindowContentRegionMax().X - PanelGutter;
        ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), rightEdge - ImGui.CalcTextSize(text).X));
        ImGui.TextColored(color, text);
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
                input, direction, plugin.GetRelayToken(), plugin.Configuration.ClientId, token);
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
            try
            {
                await historyService.AppendAsync(
                    direction, input, result, plugin.Configuration.HistoryMaxMegabytes,
                    lifetimeCancellation.Token);
            }
            catch (OperationCanceledException) when (disposed || lifetimeCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Could not record Woodword translation history");
                QueueStatus(direction, "The meaning returned, but its echo could not be recorded.");
            }
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

    private static string NormalizeForTranslation(string text) => string.Join("\n\n",
        text.Replace("\r", string.Empty)
            .Split("\n\n", StringSplitOptions.None)
            .Select(paragraph => string.Join(' ', paragraph
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))));

    private static string WrapForDisplay(string text, float width)
    {
        var paragraphs = UnwrapDisplayText(text).Split("\n\n", StringSplitOptions.None);
        var wrapped = new List<string>(paragraphs.Length);
        foreach (var paragraph in paragraphs)
        {
            var lines = new List<string>();
            var line = new StringBuilder();
            var tokenStart = 0;
            while (tokenStart < paragraph.Length)
            {
                var isWhitespace = char.IsWhiteSpace(paragraph[tokenStart]);
                var tokenEnd = tokenStart + 1;
                while (tokenEnd < paragraph.Length && char.IsWhiteSpace(paragraph[tokenEnd]) == isWhitespace)
                    tokenEnd++;

                var token = paragraph[tokenStart..tokenEnd];
                if (!isWhitespace && line.Length > 0 &&
                    ImGui.CalcTextSize(line.ToString() + token).X > width)
                {
                    lines.Add(line.ToString().TrimEnd());
                    line.Clear();
                }
                line.Append(token);
                tokenStart = tokenEnd;
            }
            if (line.Length > 0 || paragraph.Length == 0) lines.Add(line.ToString());
            wrapped.Add(string.Join('\n', lines));
        }
        return string.Join("\n\n", wrapped);
    }

    private static string UnwrapDisplayText(string text) => string.Join("\n\n",
        text.Replace("\r", string.Empty)
            .Split("\n\n", StringSplitOptions.None)
            .Select(paragraph => paragraph.Replace('\n', ' ')));

    private static int WrapDuringEdit(ImGuiInputTextCallbackDataPtr data, float width)
    {
        var text = Encoding.UTF8.GetString(data.BufTextSpan);
        var cursorText = Encoding.UTF8.GetString(data.BufTextSpan[..Math.Min(data.CursorPos, data.BufTextLen)]);
        var logicalCursor = cursorText.Count(character => character != '\r' && character != '\n');
        var wrapped = WrapForDisplay(text, width);
        if (wrapped == text) return 0;

        data.DeleteChars(0, data.BufTextLen);
        data.InsertChars(0, wrapped);
        var displayCharacterCursor = FindDisplayCursor(wrapped, logicalCursor);
        var displayByteCursor = Encoding.UTF8.GetByteCount(wrapped.AsSpan(0, displayCharacterCursor));
        data.CursorPos = displayByteCursor;
        data.SelectionStart = displayByteCursor;
        data.SelectionEnd = displayByteCursor;
        return 0;
    }

    private static int CountLogicalCharacters(string text, int displayCursor)
    {
        var count = 0;
        for (var index = 0; index < Math.Min(displayCursor, text.Length); index++)
        {
            if (text[index] != '\r' && text[index] != '\n') count++;
        }
        return count;
    }

    private static int FindDisplayCursor(string text, int logicalCursor)
    {
        var logical = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r' || text[index] == '\n') continue;
            if (logical == logicalCursor) return index;
            logical++;
        }
        return text.Length;
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
        liveChatService.TranslationReceived -= OnLiveTranslationReceived;
        liveChatService.StatusChanged -= OnLiveStatusChanged;
        lifetimeCancellation.Cancel();
        commonRequestCancellation?.Cancel();
        vieranRequestCancellation?.Cancel();
        commonRequestCancellation?.Dispose();
        vieranRequestCancellation?.Dispose();
        lifetimeCancellation.Dispose();
    }
}
