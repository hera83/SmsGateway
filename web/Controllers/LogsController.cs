using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using web.Models.Logs;
using web.Services.SmsService.Dtos.Logs;
using web.Services.SmsService.Interfaces;

namespace web.Controllers;

[Authorize]
public class LogsController(ISmsService smsService) : Controller
{
    public async Task<IActionResult> Index(LogsIndexViewModel model)
    {
        model.Page = Math.Max(1, model.Page);
        model.PageSize = Math.Clamp(model.PageSize, 10, 200);

        var request = new SearchLogsRequestDto
        {
            Level = model.Level,
            Q = model.Q,
            From = model.From,
            To = model.To,
            Page = model.Page,
            PageSize = model.PageSize
        };

        model.Result = await smsService.SearchLogsAsync(request);
        return View(model);
    }
}
