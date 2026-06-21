using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using web.Models.Subscriptions;
using web.Services.SmsService.Dtos.Subscriptions;
using web.Services.SmsService.Interfaces;

namespace web.Controllers;

[Authorize]
public class SubscriptionsController(ISmsService smsService) : Controller
{
    public async Task<IActionResult> Index(
        string? filterPhoneNumber,
        string? filterIsActive,
        Guid? filterApiKeyId)
    {
        bool? isActive = filterIsActive switch
        {
            "active" => true,
            "inactive" => false,
            _ => null
        };

        var subscriptions = await smsService.GetAllSubscriptionsAsync(
            phoneNumber: filterPhoneNumber,
            isActive: isActive,
            apiKeyId: filterApiKeyId);

        var apiKeys = await smsService.GetAllKeysAsync();

        return View(new SubscriptionsIndexViewModel
        {
            Subscriptions = subscriptions,
            ApiKeys = apiKeys,
            FilterPhoneNumber = filterPhoneNumber,
            FilterIsActive = filterIsActive,
            FilterApiKeyId = filterApiKeyId
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SubscriptionsCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Ugyldige inputværdier.";
            return RedirectToAction(nameof(Index));
        }

        var phoneNumbers = ParsePhoneNumbers(model.PhoneNumbersRaw);
        if (phoneNumbers.Count == 0)
        {
            TempData["Error"] = "Mindst ét telefonnummer er påkrævet.";
            return RedirectToAction(nameof(Index));
        }

        var request = new CreateSubscriptionsRequestDto
        {
            PhoneNumbers = phoneNumbers,
            StartDate = model.StartDate,
            EndDate = model.EndDate,
            WebhookUrl = string.IsNullOrWhiteSpace(model.WebhookUrl) ? null : model.WebhookUrl.Trim(),
            TargetApiKeyId = model.ApiKeyId
        };

        var result = await smsService.CreateSubscriptionAsync(request);
        TempData[result is not null ? "Success" : "Error"] =
            result is not null ? "Abonnement oprettet." : "Abonnement kunne ikke oprettes.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(SubscriptionsEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Ugyldige inputværdier.";
            return RedirectToAction(nameof(Index));
        }

        var phoneNumbers = ParsePhoneNumbers(model.PhoneNumbersRaw);
        if (phoneNumbers.Count == 0)
        {
            TempData["Error"] = "Mindst ét telefonnummer er påkrævet.";
            return RedirectToAction(nameof(Index));
        }

        var request = new UpdateSubscriptionsRequestDto
        {
            PhoneNumbers = phoneNumbers,
            StartDate = model.StartDate,
            EndDate = model.EndDate,
            WebhookUrl = string.IsNullOrWhiteSpace(model.WebhookUrl) ? null : model.WebhookUrl.Trim()
        };

        var result = await smsService.UpdateSubscriptionAsync(model.Id, request);
        TempData[result is not null ? "Success" : "Error"] =
            result is not null ? "Abonnement opdateret." : "Abonnement kunne ikke opdateres.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await smsService.DeleteSubscriptionAsync(id);
        TempData[deleted ? "Success" : "Error"] = deleted ? "Abonnement slettet." : "Abonnement ikke fundet.";
        return RedirectToAction(nameof(Index));
    }

    private static List<string> ParsePhoneNumbers(string raw) =>
        raw.Split(['\n', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
           .Where(s => !string.IsNullOrWhiteSpace(s))
           .Distinct()
           .ToList();
}
