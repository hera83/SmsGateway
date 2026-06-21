using web.Services.SmsService.Dtos.Sms;

namespace web.Models.Sms;

public sealed class SmsIndexViewModel
{
    public IReadOnlyList<ReadSmsResponseDto> Messages { get; set; } = [];
    public string? FilterPhoneNumber { get; set; }
    public SmsDeviceInfoResponseDto? DeviceInfo { get; set; }
}
