namespace api.Dtos.Sms;

public sealed class RetryWebhookSmsResponseDto
{
    public Guid MessageId { get; init; }
    public bool Delivered { get; init; }
    public string WebhookUrl { get; init; } = string.Empty;
    public string? FailureReason { get; init; }
}
