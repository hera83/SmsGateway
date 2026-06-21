using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using web.Models.Dashboard;
using web.Services.SmsService.Dtos.Cost;
using web.Services.SmsService.Dtos.Logs;
using web.Services.SmsService.Interfaces;

namespace web.Controllers;

[Authorize]
public class DashboardController(ISmsService smsService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var now = DateTime.Now;
        var firstOfMonth = new DateTime(now.Year, now.Month, 1);

        var healthTask = smsService.GetHealthAsync();
        var deviceTask = smsService.GetSmsDeviceInfoAsync();
        var monthlyUsageTask = smsService.GetGlobalUsageReportCostAsync(new GetUsageReportCostRequestDto
        {
            CreatedFrom = firstOfMonth,
            CreatedTo = now,
            PageNumber = 1,
            PageSize = 1
        });
        var costTask = smsService.GetCurrentCostAsync();
        var usageTask = smsService.GetGlobalUsageReportCostAsync(new GetUsageReportCostRequestDto
        {
            CreatedFrom = now.AddDays(-30),
            CreatedTo = now,
            PageNumber = 1,
            PageSize = 1
        });
        var logsTask = smsService.SearchLogsAsync(new SearchLogsRequestDto
        {
            Level = "Error",
            Page = 1,
            PageSize = 5
        });

        await Task.WhenAll(healthTask, deviceTask, monthlyUsageTask, costTask, usageTask, logsTask);

        var vm = new DashboardViewModel
        {
            Health = await healthTask,
            DeviceInfo = await deviceTask,
            MonthlyUsage = await monthlyUsageTask,
            CurrentCost = await costTask,
            UsageReport = await usageTask,
            RecentErrors = (await logsTask)?.Items ?? []
        };

        return View(vm);
    }
}
