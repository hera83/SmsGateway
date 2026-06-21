namespace api.Dtos.Cost;

public sealed class GetGlobalBalanceCostResponseDto
{
    public decimal Balance { get; set; }
    public int KeyCount { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
