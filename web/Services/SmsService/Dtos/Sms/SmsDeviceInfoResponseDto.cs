namespace web.Services.SmsService.Dtos.Sms;

public sealed class SmsDeviceInfoResponseDto
{
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Revision { get; set; } = string.Empty;
    public string Imei { get; set; } = string.Empty;
    public string Imsi { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public int? SignalQuality { get; set; }
    public string? NetworkRegistration { get; set; }
    public string? SmsCenterNumber { get; set; }
    public string? OperatorSelection { get; set; }
    public string? OperatorName { get; set; }
    public string? SimIccid { get; set; }
}
