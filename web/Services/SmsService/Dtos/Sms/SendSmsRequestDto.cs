namespace web.Services.SmsService.Dtos.Sms;

public sealed class SendSmsRequestDto
{
    public string To { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}