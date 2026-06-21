namespace api.Models;

public class Subscription
{
    public Guid Id { get; set; }
    public Guid ApiKeyId { get; set; }
    public ApiKey ApiKey { get; set; } = null!;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? WebhookUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<SubscriptionNumber> Numbers { get; set; } = new List<SubscriptionNumber>();
}
