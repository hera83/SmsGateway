using System.Text.Json.Serialization;

namespace api.Services.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SmsMessageStatus
{
    All,
    ReceivedUnread,
    ReceivedRead,
    StoredUnsent,
    StoredSent
}

public sealed class SmsDeviceInfo
{
    public string Manufacturer { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string Revision { get; init; } = string.Empty;
    public string Imei { get; init; } = string.Empty;
    public string Imsi { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }

    public int? SignalQuality { get; set; }
    public string? NetworkRegistration { get; set; }
    public string? SmsCenterNumber { get; set; }

    public string? OperatorSelection { get; set; }
    public string? OperatorName { get; set; }
    public string? SimIccid { get; set; }
}

public sealed class SmsSendResult
{
    public Guid MessageId { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime QueuedAt { get; init; }
    public int? ModemReference { get; init; }
    public IReadOnlyList<string> ModemResponse { get; init; } = Array.Empty<string>();
}

public sealed class SmsMessage
{
    public int Index { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? Originator { get; init; }
    public DateTimeOffset? Timestamp { get; init; }
    public string Body { get; init; } = string.Empty;
    public string RawHeader { get; init; } = string.Empty;
}

public sealed record WebhookSendResult(bool Success, string? FailureReason);
