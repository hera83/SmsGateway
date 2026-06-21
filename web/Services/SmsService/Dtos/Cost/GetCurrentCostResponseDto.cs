namespace web.Services.SmsService.Dtos.Cost;

public sealed class GetCurrentCostResponseDto
{
    public decimal SmsPriceDkk { get; set; }
    public DateTime UpdatedAt { get; set; }
}
