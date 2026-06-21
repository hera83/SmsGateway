using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using web.Models.Keys;
using web.Services.SmsService.Dtos.Keys;
using web.Services.SmsService.Interfaces;

namespace web.Controllers;

[Authorize]
public class KeysController(ISmsService smsService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var keys = await smsService.GetAllKeysAsync();
        return View(new KeysIndexViewModel { Keys = keys });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(KeysCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Ugyldige inputværdier.";
            return RedirectToAction(nameof(Index));
        }

        var request = new CreateKeysRequestDto
        {
            Name = model.Name,
            Balance = model.Balance,
            ResponsibleName = model.ResponsibleName,
            ResponsibleEmail = model.ResponsibleEmail
        };

        var result = await smsService.CreateKeyAsync(request);
        if (result is null)
        {
            TempData["Error"] = "Nøgle kunne ikke oprettes.";
            return RedirectToAction(nameof(Index));
        }

        TempData["NewApiKey"] = result.ApiKey;
        TempData["Success"] = $"API-nøgle \"{result.Name}\" oprettet.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(KeysEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Ugyldige inputværdier.";
            return RedirectToAction(nameof(Index));
        }

        var request = new UpdateKeysRequestDto
        {
            Id = model.Id,
            Name = model.Name,
            IsActive = model.IsActive,
            Balance = model.Balance,
            ResponsibleName = model.ResponsibleName,
            ResponsibleEmail = model.ResponsibleEmail
        };

        var result = await smsService.UpdateKeyAsync(model.Id, request);
        if (result is null)
        {
            TempData["Error"] = "Nøgle kunne ikke opdateres.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = $"API-nøgle \"{result.Name}\" opdateret.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await smsService.DeleteKeyAsync(new DeleteKeysRequestDto { Id = id });
        TempData[deleted ? "Success" : "Error"] = deleted ? "API-nøgle slettet." : "Nøgle ikke fundet.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rollover(Guid id)
    {
        var result = await smsService.RolloverKeyAsync(id);
        if (result is null)
        {
            TempData["Error"] = "Rollover mislykkedes.";
            return RedirectToAction(nameof(Index));
        }

        TempData["NewApiKey"] = result.ApiKey;
        TempData["Success"] = $"Ny nøgle genereret for \"{result.Name}\".";
        return RedirectToAction(nameof(Index));
    }
}
