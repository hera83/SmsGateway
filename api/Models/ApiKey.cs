namespace api.Models;

public class ApiKey
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public decimal Balance { get; set; }
    public string ResponsibleName { get; set; } = string.Empty;
    public string ResponsibleEmail { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
    public ICollection<SmsRecord> SmsRecords { get; set; } = new List<SmsRecord>();
    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
}
