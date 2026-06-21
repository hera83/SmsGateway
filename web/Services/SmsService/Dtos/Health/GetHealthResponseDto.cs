namespace web.Services.SmsService.Dtos.Health;

public sealed class GetHealthResponseDto
{
    public string Status { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
