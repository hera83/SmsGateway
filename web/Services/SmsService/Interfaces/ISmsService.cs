using web.Services.SmsService.Dtos.Cost;
using web.Services.SmsService.Dtos.Health;
using web.Services.SmsService.Dtos.Keys;
using web.Services.SmsService.Dtos.Logs;
using web.Services.SmsService.Dtos.Sms;
using web.Services.SmsService.Dtos.Subscriptions;

namespace web.Services.SmsService.Interfaces
{
    public interface ISmsService
    {
        Task<GetHealthResponseDto?> GetHealthAsync(CancellationToken cancellationToken = default);

        Task<IReadOnlyList<GetAllKeysResponseDto>> GetAllKeysAsync(string? apiKey = null, CancellationToken cancellationToken = default);
        Task<CreateKeysResponseDto?> CreateKeyAsync(CreateKeysRequestDto request, string? apiKey = null, CancellationToken cancellationToken = default);
        Task<GetByIdKeysResponseDto?> GetKeyByIdAsync(GetByIdKeysRequestDto request, string? apiKey = null, CancellationToken cancellationToken = default);
        Task<UpdateKeysResponseDto?> UpdateKeyAsync(Guid id, UpdateKeysRequestDto request, string? apiKey = null, CancellationToken cancellationToken = default);
        Task<bool> DeleteKeyAsync(DeleteKeysRequestDto request, string? apiKey = null, CancellationToken cancellationToken = default);
        Task<RolloverKeysResponseDto?> RolloverKeyAsync(Guid id, string? apiKey = null, CancellationToken cancellationToken = default);

        Task<GetCurrentCostResponseDto?> GetCurrentCostAsync(string? apiKey = null, CancellationToken cancellationToken = default);
        Task<UpdateCostResponseDto?> UpdateCostAsync(UpdateCostRequestDto request, string? apiKey = null, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<GetHistoryCostResponseDto>> GetCostHistoryAsync(string? apiKey = null, CancellationToken cancellationToken = default);
        Task<GetUsageReportCostResponseDto?> GetUsageReportCostAsync(GetUsageReportCostRequestDto request, string? apiKey = null, CancellationToken cancellationToken = default);
        Task<GetBalanceCostResponseDto?> GetBalanceCostAsync(string? apiKey = null, CancellationToken cancellationToken = default);
        Task<GetGlobalBalanceCostResponseDto?> GetGlobalBalanceCostAsync(string? apiKey = null, CancellationToken cancellationToken = default);
        Task<GetGlobalUsageReportCostResponseDto?> GetGlobalUsageReportCostAsync(GetUsageReportCostRequestDto request, string? apiKey = null, CancellationToken cancellationToken = default);

        Task<SearchLogsResponseDto?> SearchLogsAsync(SearchLogsRequestDto request, string? apiKey = null, CancellationToken cancellationToken = default);

        Task<SendSmsResponseDto?> SendSmsAsync(SendSmsRequestDto request, string? apiKey = null, CancellationToken cancellationToken = default);
        Task<GetStatusSmsResponseDto?> GetSmsStatusAsync(GetStatusSmsRequestDto request, string? apiKey = null, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ReadSmsResponseDto>> ReadSmsAsync(string? phoneNumber = null, string? apiKey = null, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<GetAllSubscriptionsResponseDto>> GetAllSubscriptionsAsync(string? phoneNumber = null, bool? isActive = null, Guid? apiKeyId = null, string? apiKey = null, CancellationToken cancellationToken = default);
        Task<GetByIdSubscriptionsResponseDto?> GetSubscriptionByIdAsync(Guid id, string? apiKey = null, CancellationToken cancellationToken = default);
        Task<CreateSubscriptionsResponseDto?> CreateSubscriptionAsync(CreateSubscriptionsRequestDto request, string? apiKey = null, CancellationToken cancellationToken = default);
        Task<UpdateSubscriptionsResponseDto?> UpdateSubscriptionAsync(Guid id, UpdateSubscriptionsRequestDto request, string? apiKey = null, CancellationToken cancellationToken = default);
        Task<bool> DeleteSubscriptionAsync(Guid id, string? apiKey = null, CancellationToken cancellationToken = default);
        Task<bool> DeleteSmsAsync(Guid id, string? apiKey = null, CancellationToken cancellationToken = default);
        Task<SmsDeviceInfoResponseDto?> GetSmsDeviceInfoAsync(string? apiKey = null, CancellationToken cancellationToken = default);
    }
}
