using api.Services.Models;

namespace api.Services.Interfaces;

public interface IWebhookSender
{
    Task<WebhookSendResult> SendAsync(string webhookUrl, object payload, CancellationToken cancellationToken = default);
}
