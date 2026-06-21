using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using web.Models.Cost;
using web.Services.SmsService.Dtos.Cost;
using web.Services.SmsService.Interfaces;

namespace web.Controllers;

[Authorize]
public class CostController(ISmsService smsService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var current = await smsService.GetCurrentCostAsync();
        var history = await smsService.GetCostHistoryAsync();
        return View(new CostIndexViewModel { CurrentCost = current, History = history });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(CostUpdateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Ugyldig pris.";
            return RedirectToAction(nameof(Index));
        }

        var result = await smsService.UpdateCostAsync(new UpdateCostRequestDto { SmsPriceDkk = model.SmsPriceDkk });
        if (result is null)
        {
            TempData["Error"] = "Pris kunne ikke opdateres.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = $"SMS-pris opdateret til {result.SmsPriceDkk:N4} kr.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> UsageReport(CostUsageReportViewModel model)
    {
        model.PageNumber = Math.Max(1, model.PageNumber);
        model.PageSize = Math.Clamp(model.PageSize, 10, 200);

        var request = new GetUsageReportCostRequestDto
        {
            CreatedFrom = model.CreatedFrom,
            CreatedTo = model.CreatedTo,
            Status = model.Status,
            ToPhoneNumber = model.ToPhoneNumber,
            MessageContains = model.MessageContains,
            PageNumber = model.PageNumber,
            PageSize = model.PageSize
        };

        model.Result = await smsService.GetUsageReportCostAsync(request);
        return View(model);
    }
}
