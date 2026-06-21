using web.Services.SmsService.Dtos.Cost;

namespace web.Models.Cost;

public sealed class CostIndexViewModel
{
    public GetCurrentCostResponseDto? CurrentCost { get; set; }
    public IReadOnlyList<GetHistoryCostResponseDto> History { get; set; } = [];
}
