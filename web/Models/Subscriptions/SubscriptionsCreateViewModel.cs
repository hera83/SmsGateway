using System.ComponentModel.DataAnnotations;

namespace web.Models.Subscriptions;

public sealed class SubscriptionsCreateViewModel
{
    [Required]
    public Guid ApiKeyId { get; set; }

    [Required]
    public string PhoneNumbersRaw { get; set; } = string.Empty;

    [Required]
    public DateOnly StartDate { get; set; }

    [Required]
    public DateOnly EndDate { get; set; }

    public string? WebhookUrl { get; set; }
}
