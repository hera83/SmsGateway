namespace api.Models;

public class CostConfiguration
{
    public Guid Id { get; set; }
    public decimal SmsPriceDkk { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
