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

        try
        {
            await smsService.SendSmsAsync(new SendSmsRequestDto
            {
                To = model.ToPhoneNumber,
                Message = model.Message
            });
            TempData["Success"] = $"SMS sendt til {model.ToPhoneNumber}.";
        }
        catch (HttpRequestException ex)
        {
            TempData["Error"] = ex.Extract("SMS kunne ikke sendes.");
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var deleted = await smsService.DeleteSmsAsync(id);
            TempData[deleted ? "Success" : "Error"] = deleted ? "SMS slettet." : "SMS ikke fundet.";
        }
        catch (HttpRequestException ex)
        {
            TempData["Error"] = ex.Extract("SMS kunne ikke slettes.");
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetryWebhook(Guid id)
    {
        try
        {
            var result = await smsService.RetryWebhookAsync(id);
            if (result?.Delivered == true)
            {
                TempData["Success"] = "Webhook afleveret – SMS'en er slettet fra gatewayen.";
            }
            else
            {
                TempData["Error"] = $"Webhook fejlede stadig: {result?.FailureReason ?? "ukendt fejl"}";
            }
        }
        catch (HttpRequestException ex)
        {
            TempData["Error"] = ex.Extract("Webhook kunne ikke genudløses.");
        }

        return RedirectToAction(nameof(Index));
    }
}
