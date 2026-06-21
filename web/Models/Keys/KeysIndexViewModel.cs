using web.Services.SmsService.Dtos.Keys;

namespace web.Models.Keys;

public sealed class KeysIndexViewModel
{
    public IReadOnlyList<GetAllKeysResponseDto> Keys { get; set; } = [];
}
