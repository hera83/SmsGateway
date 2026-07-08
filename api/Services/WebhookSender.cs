using System.Net.Http.Json;
using api.Services.Interfaces;
using api.Services.Models;

namespace api.Services;

public sealed class WebhookSender(IHttpClientFactory httpClientFactory, ILogger<WebhookSender> logger) : IWebhookSender
{
    private const int MaxFailureReasonLength = 1000;

    public async Task<WebhookSendResult> SendAsync(string webhookUrl, object payload, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient("Webhook");
            var response = await client.PostAsJsonAsync(webhookUrl, payload, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new WebhookSendResult(true, null);
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "Webhook to {WebhookUrl} responded with {StatusCode}: {ResponseBody}",
                webhookUrl,
                (int)response.StatusCode,
                responseBody);

            return new WebhookSendResult(false, Truncate($"HTTP {(int)response.StatusCode} {response.StatusCode}: {responseBody}"));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send webhook to {WebhookUrl}.", webhookUrl);
            return new WebhookSendResult(false, Truncate(ex.Message));
        }
    }

    private static string Truncate(string value) =>
        value.Length <= MaxFailureReasonLength ? value : value[..MaxFailureReasonLength];
}
