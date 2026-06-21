using System.ComponentModel.DataAnnotations;

namespace api.Dtos.Subscriptions;

public sealed class CreateSubscriptionsRequestDto
{
    [Required]
    public List<string> PhoneNumbers { get; set; } = new();

    [Required]
    public DateOnly StartDate { get; set; }

    [Required]
    public DateOnly EndDate { get; set; }

    public string? WebhookUrl { get; set; }

    public Guid? TargetApiKeyId { get; set; }
}
