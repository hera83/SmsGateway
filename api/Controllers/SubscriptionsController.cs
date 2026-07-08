using api.Data;
using api.Dtos.Errors;
using api.Dtos.Subscriptions;
using api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace api.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
[Authorize]
public class SubscriptionsController(AppDbContext dbContext) : ControllerBase
{
    private static readonly Regex DanishPhoneRegex = new("^[2-9][0-9]{7}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<GetAllSubscriptionsResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(UnauthorizedErrorDto), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IReadOnlyList<GetAllSubscriptionsResponseDto>>> GetAll(
        [FromQuery] string? phoneNumber = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] Guid? apiKeyId = null,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var currentApiKeyId = TryGetApiKeyId();
        var isMasterKey = IsMasterKey();

        IQueryable<Subscription> query = dbContext.Subscriptions
            .AsNoTracking()
            .Include(s => s.Numbers);

        if (!isMasterKey && currentApiKeyId.HasValue)
        {
            query = query.Where(s => s.ApiKeyId == currentApiKeyId.Value);
        }
        else if (isMasterKey && apiKeyId.HasValue)
        {
            query = query.Where(s => s.ApiKeyId == apiKeyId.Value);
        }

        if (!string.IsNullOrWhiteSpace(phoneNumber))
        {
            var normalized = NormalizeDanishPhone(phoneNumber);
            query = query.Where(s => s.Numbers.Any(n => n.PhoneNumber == normalized));
        }

        if (isActive.HasValue)
        {
            if (isActive.Value)
                query = query.Where(s => s.StartDate <= today && s.EndDate >= today);
            else
                query = query.Where(s => s.StartDate > today || s.EndDate < today);
        }

        var subscriptions = await query
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new GetAllSubscriptionsResponseDto
            {
                Id = s.Id,
                ApiKeyId = s.ApiKeyId,
                PhoneNumbers = s.Numbers.Select(n => n.PhoneNumber).ToList(),
                StartDate = s.StartDate,
                EndDate = s.EndDate,
                WebhookUrl = s.WebhookUrl,
                IsActive = s.StartDate <= today && s.EndDate >= today,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(subscriptions);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(GetByIdSubscriptionsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotFoundErrorDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(UnauthorizedErrorDto), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ForbiddenErrorDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GetByIdSubscriptionsResponseDto>> GetById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var currentApiKeyId = TryGetApiKeyId();
        var isMasterKey = IsMasterKey();

        var subscription = await dbContext.Subscriptions
            .AsNoTracking()
            .Include(s => s.Numbers)
            .Where(s => s.Id == id)
            .Select(s => new GetByIdSubscriptionsResponseDto
            {
                Id = s.Id,
                ApiKeyId = s.ApiKeyId,
                PhoneNumbers = s.Numbers.Select(n => n.PhoneNumber).ToList(),
                StartDate = s.StartDate,
                EndDate = s.EndDate,
                WebhookUrl = s.WebhookUrl,
                IsActive = s.StartDate <= today && s.EndDate >= today,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is null)
        {
            return NotFound(new NotFoundErrorDto
            {
                Message = "Subscription not found.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        if (!isMasterKey && subscription.ApiKeyId != currentApiKeyId)
        {
            return Forbid();
        }

        return Ok(subscription);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CreateSubscriptionsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(BadRequestErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ValidationErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ConflictErrorDto), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(UnauthorizedErrorDto), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ForbiddenErrorDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CreateSubscriptionsResponseDto>> Create(
        [FromBody] CreateSubscriptionsRequestDto request,
        CancellationToken cancellationToken)
    {
        var apiKeyId = TryGetApiKeyId();
        var isMasterKey = IsMasterKey();

        if (apiKeyId is null && !isMasterKey)
        {
            return Forbid();
        }

        if (isMasterKey)
        {
            if (request.TargetApiKeyId is null)
            {
                return BadRequest(new BadRequestErrorDto
                {
                    Message = "TargetApiKeyId is required when creating a subscription as master key.",
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            var keyExists = await dbContext.ApiKeys.AnyAsync(k => k.Id == request.TargetApiKeyId.Value, cancellationToken);
            if (!keyExists)
            {
                return NotFound(new NotFoundErrorDto
                {
                    Message = "The specified TargetApiKeyId does not exist.",
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            apiKeyId = request.TargetApiKeyId.Value;
        }

        if (request.PhoneNumbers is null || request.PhoneNumbers.Count == 0)
        {
            return BadRequest(new BadRequestErrorDto
            {
                Message = "At least one phone number is required.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        if (request.EndDate < request.StartDate)
        {
            return BadRequest(new BadRequestErrorDto
            {
                Message = "EndDate must be on or after StartDate.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        var normalizedNumbers = request.PhoneNumbers
            .Select(NormalizeDanishPhone)
            .Distinct()
            .ToList();

        var invalidNumbers = normalizedNumbers.Where(n => !DanishPhoneRegex.IsMatch(n)).ToList();
        if (invalidNumbers.Count > 0)
        {
            return BadRequest(new BadRequestErrorDto
            {
                Message = $"The following numbers are not valid Danish phone numbers: {string.Join(", ", invalidNumbers)}.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        if (!string.IsNullOrWhiteSpace(request.WebhookUrl) && !Uri.TryCreate(request.WebhookUrl, UriKind.Absolute, out _))
        {
            return BadRequest(new BadRequestErrorDto
            {
                Message = "WebhookUrl is not a valid absolute URL.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var effectiveEndDate = request.EndDate;

        foreach (var number in normalizedNumbers)
        {
            var conflicts = await dbContext.SubscriptionNumbers
                .Where(sn => sn.PhoneNumber == number && sn.Subscription.ApiKeyId != apiKeyId)
                .Select(sn => new
                {
                    sn.Subscription.StartDate,
                    sn.Subscription.EndDate
                })
                .ToListAsync(cancellationToken);

            foreach (var conflict in conflicts)
            {
                if (conflict.StartDate <= today && conflict.EndDate >= today)
                {
                    return Conflict(new ConflictErrorDto
                    {
                        Message = $"Phone number {number} is currently active under another key and cannot be added.",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                if (request.StartDate <= conflict.EndDate && effectiveEndDate >= conflict.StartDate)
                {
                    var cappedEnd = conflict.StartDate.AddDays(-1);
                    if (cappedEnd < request.StartDate)
                    {
                        return Conflict(new ConflictErrorDto
                        {
                            Message = $"Phone number {number} has a conflicting future subscription that leaves no valid date range.",
                            TraceId = HttpContext.TraceIdentifier
                        });
                    }

                    if (cappedEnd < effectiveEndDate)
                    {
                        effectiveEndDate = cappedEnd;
                    }
                }
            }
        }

        var existing = await dbContext.Subscriptions
            .Where(s => s.ApiKeyId == apiKeyId)
            .Include(s => s.Numbers)
            .ToListAsync(cancellationToken);

        dbContext.Subscriptions.RemoveRange(existing);

        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            ApiKeyId = apiKeyId.Value,
            StartDate = request.StartDate,
            EndDate = effectiveEndDate,
            WebhookUrl = string.IsNullOrWhiteSpace(request.WebhookUrl) ? null : request.WebhookUrl.Trim(),
            CreatedAt = DateTime.Now,
            Numbers = normalizedNumbers.Select(n => new SubscriptionNumber
            {
                Id = Guid.NewGuid(),
                PhoneNumber = n
            }).ToList()
        };

        dbContext.Subscriptions.Add(subscription);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new CreateSubscriptionsResponseDto
        {
            SubscriptionId = subscription.Id,
            ApiKeyId = subscription.ApiKeyId,
            PhoneNumbers = normalizedNumbers,
            StartDate = subscription.StartDate,
            EndDate = subscription.EndDate,
            WebhookUrl = subscription.WebhookUrl,
            CreatedAt = subscription.CreatedAt
        });
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(UpdateSubscriptionsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(BadRequestErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ValidationErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(NotFoundErrorDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ConflictErrorDto), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(UnauthorizedErrorDto), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ForbiddenErrorDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<UpdateSubscriptionsResponseDto>> Update(
        [FromRoute] Guid id,
        [FromBody] UpdateSubscriptionsRequestDto request,
        CancellationToken cancellationToken)
    {
        var currentApiKeyId = TryGetApiKeyId();
        var isMasterKey = IsMasterKey();

        var subscription = await dbContext.Subscriptions
            .Include(s => s.Numbers)
            .Where(s => s.Id == id)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is null)
        {
            return NotFound(new NotFoundErrorDto
            {
                Message = "Subscription not found.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        if (!isMasterKey && subscription.ApiKeyId != currentApiKeyId)
        {
            return Forbid();
        }

        if (request.PhoneNumbers is null || request.PhoneNumbers.Count == 0)
        {
            return BadRequest(new BadRequestErrorDto
            {
                Message = "At least one phone number is required.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        if (request.EndDate < request.StartDate)
        {
            return BadRequest(new BadRequestErrorDto
            {
                Message = "EndDate must be on or after StartDate.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        var normalizedNumbers = request.PhoneNumbers
            .Select(NormalizeDanishPhone)
            .Distinct()
            .ToList();

        var invalidNumbers = normalizedNumbers.Where(n => !DanishPhoneRegex.IsMatch(n)).ToList();
        if (invalidNumbers.Count > 0)
        {
            return BadRequest(new BadRequestErrorDto
            {
                Message = $"The following numbers are not valid Danish phone numbers: {string.Join(", ", invalidNumbers)}.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        if (!string.IsNullOrWhiteSpace(request.WebhookUrl) && !Uri.TryCreate(request.WebhookUrl, UriKind.Absolute, out _))
        {
            return BadRequest(new BadRequestErrorDto
            {
                Message = "WebhookUrl is not a valid absolute URL.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var effectiveEndDate = request.EndDate;

        foreach (var number in normalizedNumbers)
        {
            var conflicts = await dbContext.SubscriptionNumbers
                .Where(sn => sn.PhoneNumber == number && sn.SubscriptionId != id)
                .Select(sn => new
                {
                    sn.Subscription.StartDate,
                    sn.Subscription.EndDate
                })
                .ToListAsync(cancellationToken);

            foreach (var conflict in conflicts)
            {
                if (conflict.StartDate <= today && conflict.EndDate >= today)
                {
                    return Conflict(new ConflictErrorDto
                    {
                        Message = $"Phone number {number} is currently active in another subscription and cannot be updated.",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                if (request.StartDate <= conflict.EndDate && effectiveEndDate >= conflict.StartDate)
                {
                    var cappedEnd = conflict.StartDate.AddDays(-1);
                    if (cappedEnd < request.StartDate)
                    {
                        return Conflict(new ConflictErrorDto
                        {
                            Message = $"Phone number {number} has a conflicting subscription that leaves no valid date range.",
                            TraceId = HttpContext.TraceIdentifier
                        });
                    }

                    if (cappedEnd < effectiveEndDate)
                    {
                        effectiveEndDate = cappedEnd;
                    }
                }
            }
        }

        subscription.StartDate = request.StartDate;
        subscription.EndDate = effectiveEndDate;
        subscription.WebhookUrl = string.IsNullOrWhiteSpace(request.WebhookUrl) ? null : request.WebhookUrl.Trim();
        subscription.UpdatedAt = DateTime.Now;

        // Muter den allerede trackede Numbers-collection i stedet for at RemoveRange + gentildele den:
        // reassigning navigation-property'en på en tracked entity efter RemoveRange forvirrer EF Core's
        // change tracker og udløser "expected to affect 1 row(s), but actually affected 0 row(s)".
        var existingNumbers = subscription.Numbers.ToList();

        foreach (var toRemove in existingNumbers.Where(n => !normalizedNumbers.Contains(n.PhoneNumber)))
        {
            subscription.Numbers.Remove(toRemove);
        }

        // Nye SubscriptionNumber-entiteter skal tilføjes via DbContext, ikke kun navigation-collection'en:
        // når nøglen (Id) sættes manuelt og entiteten kun opdages via relationship-fixup i DetectChanges,
        // antager EF Core at rækken allerede findes og genererer en UPDATE i stedet for en INSERT, hvilket
        // udløser samme "0 rows affected"-fejl.
        foreach (var toAdd in normalizedNumbers.Where(n => existingNumbers.All(existing => existing.PhoneNumber != n)))
        {
            var newNumber = new SubscriptionNumber
            {
                Id = Guid.NewGuid(),
                SubscriptionId = subscription.Id,
                PhoneNumber = toAdd
            };

            dbContext.SubscriptionNumbers.Add(newNumber);
            subscription.Numbers.Add(newNumber);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new UpdateSubscriptionsResponseDto
        {
            SubscriptionId = subscription.Id,
            ApiKeyId = subscription.ApiKeyId,
            PhoneNumbers = normalizedNumbers,
            StartDate = subscription.StartDate,
            EndDate = subscription.EndDate,
            WebhookUrl = subscription.WebhookUrl,
            CreatedAt = subscription.CreatedAt,
            UpdatedAt = subscription.UpdatedAt
        });
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(NotFoundErrorDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(UnauthorizedErrorDto), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ForbiddenErrorDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var currentApiKeyId = TryGetApiKeyId();
        var isMasterKey = IsMasterKey();

        var subscription = await dbContext.Subscriptions
            .Where(s => s.Id == id)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is null)
        {
            return NotFound(new NotFoundErrorDto
            {
                Message = "Subscription not found.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        if (!isMasterKey && subscription.ApiKeyId != currentApiKeyId)
        {
            return Forbid();
        }

        dbContext.Subscriptions.Remove(subscription);
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private static string NormalizePhone(string phone) =>
        phone.Trim().Replace(" ", string.Empty).Replace("-", string.Empty);

    private static string NormalizeDanishPhone(string phone)
    {
        var normalized = NormalizePhone(phone);
        if (normalized.StartsWith("+45", StringComparison.Ordinal))
        {
            normalized = normalized[3..];
        }
        else if (normalized.StartsWith("45", StringComparison.Ordinal) && normalized.Length > 8)
        {
            normalized = normalized[2..];
        }

        return normalized;
    }

    private Guid? TryGetApiKeyId()
    {
        var nameIdentifier = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(nameIdentifier, out var apiKeyId))
        {
            return apiKeyId;
        }

        return null;
    }

    private bool IsMasterKey() => User.HasClaim("master_key", "true");
}
