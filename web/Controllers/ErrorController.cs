using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using web.Models;

namespace web.Controllers;

[AllowAnonymous]
public class ErrorController : Controller
{
    [Route("Error/{statusCode:int}")]
    public IActionResult StatusCode(int statusCode)
    {
        var model = statusCode switch
        {
            404 => new ErrorViewModel
            {
                StatusCode  = 404,
                Title       = "Siden blev ikke fundet",
                Description = "Den side du leder efter eksisterer ikke eller er blevet flyttet.",
                RequestId   = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            },
            403 => new ErrorViewModel
            {
                StatusCode  = 403,
                Title       = "Ingen adgang",
                Description = "Du har ikke tilladelse til at se denne side.",
                RequestId   = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            },
            401 => new ErrorViewModel
            {
                StatusCode  = 401,
                Title       = "Log ind krævet",
                Description = "Du skal være logget ind for at se denne side.",
                RequestId   = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            },
            _ => new ErrorViewModel
            {
                StatusCode  = statusCode,
                Title       = "Der opstod en fejl",
                Description = $"Serveren returnerede statuskode {statusCode}.",
                RequestId   = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            }
        };

        return View("Index", model);
    }

    [Route("Error")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Index()
    {
        var exceptionFeature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();

        var model = new ErrorViewModel
        {
            StatusCode  = 500,
            Title       = "Intern serverfejl",
            Description = "Der opstod en uventet fejl. Prøv igen eller kontakt administratoren.",
            RequestId   = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        };

        return View(model);
    }
}
