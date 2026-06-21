using web.Services.SmsService.Dtos.Cost;

namespace web.Models.Cost;

public sealed class CostUsageReportViewModel
{
    public string? Status { get; set; }
    public string? ToPhoneNumber { get; set; }
    public string? MessageContains { get; set; }
    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;

    public GetUsageReportCostResponseDto? Result { get; set; }
}
