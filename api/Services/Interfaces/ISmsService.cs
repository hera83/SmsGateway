using api.Services.Models;

namespace api.Services.Interfaces;

public interface ISmsService
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken = default);
    Task<SmsDeviceInfo> GetDeviceInfoAsync(CancellationToken cancellationToken = default);
    Task<int?> GetSignalQualityAsync(CancellationToken cancellationToken = default);
    Task<string?> GetNetworkRegistrationAsync(CancellationToken cancellationToken = default);
    Task<string?> GetSmsCenterNumberAsync(CancellationToken cancellationToken = default);

    Task<SmsSendResult> SendSmsAsync(string to, string message, CancellationToken cancellationToken = default);
    Task<SmsSendResult> SendSmsToModemAsync(string to, string message, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SmsMessage>> ListMessagesAsync(SmsMessageStatus status = SmsMessageStatus.All, CancellationToken cancellationToken = default);
    Task<SmsMessage?> ReadMessageAsync(int index, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SmsMessage>> ReadUnreadMessagesAsync(CancellationToken cancellationToken = default);

    Task DeleteMessageAsync(int index, CancellationToken cancellationToken = default);
    Task<int> DeleteAllMessagesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ExecuteRawCommandAsync(string command, CancellationToken cancellationToken = default);
}
