using web.Services.SmsService.Dtos.Cost;
using web.Services.SmsService.Dtos.Health;
using web.Services.SmsService.Dtos.Logs;
using web.Services.SmsService.Dtos.Sms;

namespace web.Models.Dashboard;

public sealed class DashboardViewModel
{
    public GetHealthResponseDto? Health { get; set; }
    public SmsDeviceInfoResponseDto? DeviceInfo { get; set; }
    public GetGlobalUsageReportCostResponseDto? MonthlyUsage { get; set; }
    public GetCurrentCostResponseDto? CurrentCost { get; set; }
    public GetGlobalUsageReportCostResponseDto? UsageReport { get; set; }
    public IReadOnlyList<SearchLogsItemResponseDto> RecentErrors { get; set; } = [];
}
