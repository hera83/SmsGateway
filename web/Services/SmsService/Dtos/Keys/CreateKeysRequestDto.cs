namespace web.Services.SmsService.Dtos.Keys;

public sealed class CreateKeysRequestDto
{
    public string Name { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public string ResponsibleName { get; set; } = string.Empty;
    public string ResponsibleEmail { get; set; } = string.Empty;
}
