namespace api.Models;

public class SubscriptionNumber
{
    public Guid Id { get; set; }
    public Guid SubscriptionId { get; set; }
    public Subscription Subscription { get; set; } = null!;
    public string PhoneNumber { get; set; } = string.Empty;
}
