namespace api.Models;

public static class SmsRecordDirections
{
    public const string Outgoing = "Outgoing";
    public const string Incoming = "Incoming";
}

public static class SmsRecordStatuses
{
    public const string Queued = "Queued";
    public const string Sending = "Sending";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
    public const string Received = "Received";
}

public class SmsRecord
{
    public Guid Id { get; set; }
    public Guid? ApiKeyId { get; set; }
    public ApiKey? ApiKey { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ToPhoneNumber { get; set; } = string.Empty;
    public string? FromPhoneNumber { get; set; }
    public string Message { get; set; } = string.Empty;
    public decimal UnitPriceDkk { get; set; }
    public int SegmentCount { get; set; }
    public decimal TotalPriceDkk { get; set; }
    public bool IsBalanceDeducted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? QueuedAt { get; set; }
    public DateTime? ProcessingStartedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public int? ModemReference { get; set; }
    public string? ModemResponseJson { get; set; }
    public string? FailureReason { get; set; }
    public int? InboxIndex { get; set; }
}
