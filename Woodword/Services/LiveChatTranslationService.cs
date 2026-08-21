using System.Threading.Channels;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using Woodword.Models;

namespace Woodword.Services;

public sealed class LiveChatTranslationService : IDisposable
{
    private sealed record PendingMessage(DateTime Timestamp, string Channel, string Sender, string Text);

    private readonly IChatGui chatGui;
    private readonly TranslationService translationService;
    private readonly Func<string> relayToken;
    private readonly Func<string> clientId;
    private readonly Func<string> liveAccessCode;
    private readonly Channel<PendingMessage> queue = Channel.CreateBounded<PendingMessage>(
        new BoundedChannelOptions(32)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
        });
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly object recentLock = new();
    private readonly Dictionary<string, DateTime> recentMessages = new(StringComparer.Ordinal);
    private readonly Task worker;
    private CancellationTokenSource? listeningCancellation;
    private bool enabled;
    private bool disposed;

    public event Action<LiveTranslationEntry>? TranslationReceived;
    public event Action<string>? StatusChanged;

    public bool IsEnabled => enabled;

    public LiveChatTranslationService(
        IChatGui chatGui,
        TranslationService translationService,
        Func<string> relayToken,
        Func<string> clientId,
        Func<string> liveAccessCode)
    {
        this.chatGui = chatGui;
        this.translationService = translationService;
        this.relayToken = relayToken;
        this.clientId = clientId;
        this.liveAccessCode = liveAccessCode;
        worker = Task.Run(ProcessQueueAsync);
    }

    public void SetEnabled(bool value)
    {
        if (disposed || enabled == value) return;
        enabled = value;
        if (enabled)
        {
            listeningCancellation?.Dispose();
            listeningCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token);
            chatGui.ChatMessage += OnChatMessage;
            StatusChanged?.Invoke("The Wood is listening to Say and custom emotes.");
        }
        else
        {
            chatGui.ChatMessage -= OnChatMessage;
            listeningCancellation?.Cancel();
            while (queue.Reader.TryRead(out _)) { }
            StatusChanged?.Invoke("Live listening is paused.");
        }
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (!enabled || message.LogKind is not (XivChatType.Say or XivChatType.CustomEmote)) return;

        var text = message.OriginalMessage.ToString().Trim();
        if (!IcelandicDetector.IsLikelyIcelandic(text)) return;

        var sender = message.OriginalSender.ToString().Trim();
        var channel = message.LogKind == XivChatType.Say ? "Say" : "Custom emote";
        var now = DateTime.Now;
        var key = $"{(int)message.LogKind}|{sender}|{text}";
        lock (recentLock)
        {
            foreach (var expired in recentMessages.Where(item => now - item.Value > TimeSpan.FromSeconds(20))
                         .Select(item => item.Key).ToArray())
                recentMessages.Remove(expired);
            if (recentMessages.ContainsKey(key)) return;
            recentMessages[key] = now;
        }

        if (!queue.Writer.TryWrite(new PendingMessage(now, channel, sender, text)))
            StatusChanged?.Invoke("The Wood is hearing too many voices at once; a line was allowed to pass.");
    }

    private async Task ProcessQueueAsync()
    {
        var token = lifetimeCancellation.Token;
        try
        {
            await foreach (var pending in queue.Reader.ReadAllAsync(token))
            {
                if (!enabled) continue;
                StatusChanged?.Invoke("The Wood is discerning Vieran words...");
                try
                {
                    var listeningToken = listeningCancellation?.Token ?? token;
                    var translated = await translationService.TranslateAsync(
                        pending.Text,
                        TranslationDirection.VieranToCommon,
                        relayToken(),
                        clientId(),
                        listeningToken,
                        liveAccessCode());
                    if (enabled)
                    {
                        TranslationReceived?.Invoke(new LiveTranslationEntry(
                            pending.Timestamp, pending.Channel, pending.Sender, pending.Text, translated));
                        StatusChanged?.Invoke("The Wood is listening to Say and custom emotes.");
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException) when (!enabled)
                {
                    continue;
                }
                catch (TranslationException ex)
                {
                    StatusChanged?.Invoke(ex.Message);
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error(ex, "Unexpected live chat translation failure");
                    StatusChanged?.Invoke("An unfamiliar silence has fallen over the listening Wood.");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(1250), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (enabled) chatGui.ChatMessage -= OnChatMessage;
        enabled = false;
        lifetimeCancellation.Cancel();
        listeningCancellation?.Cancel();
        queue.Writer.TryComplete();
        try { worker.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        lifetimeCancellation.Dispose();
        listeningCancellation?.Dispose();
    }
}
