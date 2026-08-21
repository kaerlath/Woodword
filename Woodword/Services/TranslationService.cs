using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Woodword.Models;

namespace Woodword.Services;

public sealed class TranslationService : IDisposable
{
    public const int MaximumTextLength = 4000;
    private static readonly Uri RelayUri = new("https://woodword-relay.kaerlath.workers.dev/translate");
    private static readonly Uri LiveValidationUri = new("https://woodword-relay.kaerlath.workers.dev/live/validate");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private bool disposed;

    public TranslationService()
    {
        httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Woodword", "0.1"));
    }

    public async Task<string> TranslateAsync(
        string text,
        TranslationDirection direction,
        string relayToken,
        string clientId,
        CancellationToken cancellationToken,
        string? liveAccessCode = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            throw new TranslationException("There are no words for the Wood to render.");
        if (trimmed.Length > MaximumTextLength)
            throw new TranslationException("That passage is too long for the Wood to render at once.");
        if (string.IsNullOrWhiteSpace(relayToken))
            throw new TranslationException("The Wood does not yet know your token. Open Settings to provide it.");
        if (!Guid.TryParse(clientId, out _))
            throw new TranslationException("The Wood cannot discern this installation. Reopen the plugin to renew its mark.");

        using var request = new HttpRequestMessage(HttpMethod.Post, RelayUri);
        request.Headers.TryAddWithoutValidation("X-Woodword-Token", relayToken.Trim());
        request.Headers.TryAddWithoutValidation("X-Woodword-Client", clientId);
        if (!string.IsNullOrWhiteSpace(liveAccessCode))
        {
            request.Headers.TryAddWithoutValidation("X-Woodword-Request", "live");
            request.Headers.TryAddWithoutValidation("X-Woodword-Live-Code", liveAccessCode.Trim());
        }
        request.Content = JsonContent.Create(
            new TranslationRequest(trimmed, direction.ToWireValue()),
            options: JsonOptions);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslationException("The Wood listened too long and the path faded. Please try again.");
        }
        catch (HttpRequestException)
        {
            throw new TranslationException("The Wood cannot be reached. Check your connection and try again.");
        }

        using (response)
        {
            TranslationResponse? payload = null;
            try
            {
                payload = await response.Content.ReadFromJsonAsync<TranslationResponse>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException) { }

            if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(payload?.Translation))
                return payload.Translation;

            if (!string.IsNullOrWhiteSpace(payload?.Message))
                throw new TranslationException(payload.Message);

            throw new TranslationException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "The Wood does not know your voice. Check the relay token in Settings.",
                HttpStatusCode.TooManyRequests => "The Wood asks that you wait a moment before speaking again.",
                _ when (int)response.StatusCode >= 500 => "The Wood has fallen silent for the moment.",
                _ => "The Wood could not render those words.",
            });
        }
    }

    public async Task ValidateLiveAccessAsync(
        string relayToken, string clientId, string liveAccessCode, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (string.IsNullOrWhiteSpace(liveAccessCode))
            throw new TranslationException("Enter a live-listening validation code first.");

        using var request = new HttpRequestMessage(HttpMethod.Post, LiveValidationUri);
        request.Headers.TryAddWithoutValidation("X-Woodword-Token", relayToken.Trim());
        request.Headers.TryAddWithoutValidation("X-Woodword-Client", clientId);
        request.Headers.TryAddWithoutValidation("X-Woodword-Live-Code", liveAccessCode.Trim());

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslationException("The Wood listened too long while examining that code.");
        }
        catch (HttpRequestException)
        {
            throw new TranslationException("The Wood cannot be reached to examine that code.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode) return;
            TranslationResponse? payload = null;
            try
            {
                payload = await response.Content.ReadFromJsonAsync<TranslationResponse>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException) { }
            throw new TranslationException(payload?.Message ?? (response.StatusCode switch
            {
                HttpStatusCode.Forbidden => "That validation code is not recognized by the Wood.",
                HttpStatusCode.Unauthorized => "The Wood does not know this installation's voice.",
                _ => "The Wood could not examine that validation code.",
            }));
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        httpClient.Dispose();
    }
}
