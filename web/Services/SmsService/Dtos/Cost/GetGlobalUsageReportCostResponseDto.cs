namespace web.Services.SmsService.Dtos.Cost;

public sealed class GetGlobalUsageReportCostResponseDto
{
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public decimal TotalUsageDkk { get; set; }
    public List<GetUsageReportCostItemResponseDto> Items { get; set; } = [];
}
