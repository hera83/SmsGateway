namespace api.Models;

public class CostPriceHistory
{
    public Guid Id { get; set; }
    public decimal OldSmsPriceDkk { get; set; }
    public decimal NewSmsPriceDkk { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.Now;
}
