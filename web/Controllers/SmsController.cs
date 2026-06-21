using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using web.Models.Sms;
using web.Services.SmsService.Dtos.Sms;
using web.Services.SmsService.Interfaces;

namespace web.Controllers;

[Authorize]
public class SmsController(ISmsService smsService) : Controller
{
    public async Task<IActionResult> Index(string? filterPhoneNumber)
    {
        var messages = await smsService.ReadSmsAsync(filterPhoneNumber);
        var deviceInfo = await smsService.GetSmsDeviceInfoAsync();
        return View(new SmsIndexViewModel
        {
            Messages = messages,
            FilterPhoneNumber = filterPhoneNumber,
            DeviceInfo = deviceInfo
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(SmsSendViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Ugyldig besked.";
            return RedirectToAction(nameof(Index));
        }

        var result = await smsService.SendSmsAsync(new SendSmsRequestDto
        {
            To = model.ToPhoneNumber,
            Message = model.Message
        });

        TempData[result is not null ? "Success" : "Error"] =
            result is not null ? $"SMS sendt til {model.ToPhoneNumber}." : "SMS kunne ikke sendes.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await smsService.DeleteSmsAsync(id);
        TempData[deleted ? "Success" : "Error"] = deleted ? "SMS slettet." : "SMS ikke fundet.";
        return RedirectToAction(nameof(Index));
    }
}
