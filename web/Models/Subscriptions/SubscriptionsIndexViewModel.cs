using web.Services.SmsService.Dtos.Keys;
using web.Services.SmsService.Dtos.Subscriptions;

namespace web.Models.Subscriptions;

public sealed class SubscriptionsIndexViewModel
{
    public IReadOnlyList<GetAllSubscriptionsResponseDto> Subscriptions { get; set; } = [];
    public IReadOnlyList<GetAllKeysResponseDto> ApiKeys { get; set; } = [];

    public string? FilterPhoneNumber { get; set; }
    public string? FilterIsActive { get; set; }
    public Guid? FilterApiKeyId { get; set; }
}
