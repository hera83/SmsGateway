using api.Dtos.Errors;
using api.Dtos.Sms;
using api.Data;
using api.Models;
using api.Services.Interfaces;
using api.Services.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace api.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
[Authorize]
public class SmsController(ISmsService smsService, IWebhookSender webhookSender, AppDbContext dbContext, ILogger<SmsController> logger) : ControllerBase
{
    private static readonly Regex DanishPhoneRegex = new("^[2-9][0-9]{7}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private const int StandardSmsLength = 160;

    [HttpPost]
    [ProducesResponseType(typeof(SendSmsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(BadRequestErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ValidationErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(UnauthorizedErrorDto), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ForbiddenErrorDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(PaymentRequiredErrorDto), StatusCodes.Status402PaymentRequired)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SendSmsResponseDto>> Send([FromBody] SendSmsRequestDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.To) || string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new BadRequestErrorDto
            {
                Message = "Both 'to' and 'message' are required.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        var normalizedTo = NormalizeDanishPhone(request.To);
        if (!DanishPhoneRegex.IsMatch(normalizedTo))
        {
            return BadRequest(new BadRequestErrorDto
            {
                Message = "Only Danish phone numbers are allowed.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        var smsPriceDkk = await dbContext.CostConfigurations
            .AsNoTracking()
            .Select(x => x.SmsPriceDkk)
            .FirstOrDefaultAsync(cancellationToken);

        var segmentCount = Math.Max(1, (int)Math.Ceiling(request.Message.Length / (decimal)StandardSmsLength));
        var totalPriceDkk = segmentCount * smsPriceDkk;
        var apiKeyId = TryGetApiKeyId();

        // Master key bypasser subscription-check
        if (apiKeyId is not null)
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var hasAccess = await dbContext.SubscriptionNumbers
                .AnyAsync(sn =>
                    sn.PhoneNumber == normalizedTo &&
                    sn.Subscription.ApiKeyId == apiKeyId &&
                    sn.Subscription.StartDate <= today &&
                    sn.Subscription.EndDate >= today,
                    cancellationToken);

            if (!hasAccess)
            {
                return Forbid();
            }

            var apiKey = await dbContext.ApiKeys
                .AsNoTracking()
                .Where(x => x.Id == apiKeyId.Value)
                .Select(x => new { x.Balance })
                .FirstOrDefaultAsync(cancellationToken);

            if (apiKey is null || apiKey.Balance < totalPriceDkk)
            {
                return StatusCode(StatusCodes.Status402PaymentRequired, new PaymentRequiredErrorDto
                {
                    Message = "Insufficient balance to send the SMS.",
                    TraceId = HttpContext.TraceIdentifier
                });
            }
        }

        var smsRecord = new SmsRecord
        {
            Id = Guid.NewGuid(),
            ApiKeyId = apiKeyId,
            Direction = SmsRecordDirections.Outgoing,
            Status = SmsRecordStatuses.Queued,
            ToPhoneNumber = normalizedTo,
            Message = request.Message,
            UnitPriceDkk = smsPriceDkk,
            SegmentCount = segmentCount,
            TotalPriceDkk = totalPriceDkk,
            IsBalanceDeducted = false,
            CreatedAt = DateTime.Now,
            QueuedAt = DateTime.Now
        };

        dbContext.SmsRecords.Add(smsRecord);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new SendSmsResponseDto
        {
            MessageId = smsRecord.Id,
            Status = smsRecord.Status,
            QueuedAt = smsRecord.QueuedAt ?? smsRecord.CreatedAt,
            SegmentCount = smsRecord.SegmentCount,
            UnitPriceDkk = smsRecord.UnitPriceDkk,
            TotalPriceDkk = smsRecord.TotalPriceDkk
        });
    }

    [HttpGet("{messageId:guid}")]
    [ProducesResponseType(typeof(GetStatusSmsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotFoundErrorDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(UnauthorizedErrorDto), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ForbiddenErrorDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GetStatusSmsResponseDto>> Status([FromRoute] Guid messageId, CancellationToken cancellationToken)
    {
        var status = await dbContext.SmsRecords
            .AsNoTracking()
            .Where(x => x.Id == messageId)
            .Select(x => new GetStatusSmsResponseDto
            {
                MessageId = x.Id,
                ApiKeyId = x.ApiKeyId,
                Direction = x.Direction,
                Status = x.Status,
                To = x.ToPhoneNumber,
                From = x.FromPhoneNumber,
                SegmentCount = x.SegmentCount,
                UnitPriceDkk = x.UnitPriceDkk,
                TotalPriceDkk = x.TotalPriceDkk,
                IsBalanceDeducted = x.IsBalanceDeducted,
                CreatedAt = x.CreatedAt,
                QueuedAt = x.QueuedAt,
                ProcessingStartedAt = x.ProcessingStartedAt,
                SentAt = x.SentAt,
                FailedAt = x.FailedAt,
                ReceivedAt = x.ReceivedAt,
                ModemReference = x.ModemReference,
                FailureReason = x.FailureReason
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (status is null)
        {
            return NotFound(new NotFoundErrorDto
            {
                Message = "SMS message not found.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        return Ok(status);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ReadSmsResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IReadOnlyList<ReadSmsResponseDto>>> Read([FromQuery] string? phoneNumber = null, CancellationToken cancellationToken = default)
    {
        var apiKeyId = TryGetApiKeyId();
        var today = DateOnly.FromDateTime(DateTime.Now);

        IQueryable<SmsRecord> query = dbContext.SmsRecords
            .AsNoTracking()
            .Where(x => x.Direction == SmsRecordDirections.Incoming);

        // Normal key: filtrer på aktive subscription-numre
        if (apiKeyId is not null)
        {
            var subscribedNumbers = await dbContext.SubscriptionNumbers
                .Where(sn =>
                    sn.Subscription.ApiKeyId == apiKeyId &&
                    sn.Subscription.StartDate <= today &&
                    sn.Subscription.EndDate >= today)
                .Select(sn => sn.PhoneNumber)
                .ToListAsync(cancellationToken);

            query = query.Where(x => x.FromPhoneNumber != null && subscribedNumbers.Contains(x.FromPhoneNumber));
        }

        if (!string.IsNullOrWhiteSpace(phoneNumber))
        {
            var normalizedPhone = NormalizePhoneForCompare(phoneNumber);
            query = query.Where(x => x.FromPhoneNumber == normalizedPhone);
        }

        var records = await query
            .OrderByDescending(x => x.ReceivedAt ?? x.CreatedAt)
            .Select(x => new ReadSmsResponseDto
            {
                Id = x.Id,
                Status = x.Status,
                FromPhoneNumber = x.FromPhoneNumber,
                ReceivedAt = x.ReceivedAt ?? x.CreatedAt,
                Body = x.Message,
                WebhookFailed = x.Status == SmsRecordStatuses.WebhookFailed,
                FailureReason = x.FailureReason
            })
            .ToListAsync(cancellationToken);

        return Ok(records);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(NotFoundErrorDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var record = await dbContext.SmsRecords.FindAsync([id], cancellationToken);
        if (record is null)
        {
            return NotFound(new NotFoundErrorDto { TraceId = HttpContext.TraceIdentifier });
        }

        dbContext.SmsRecords.Remove(record);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (record.InboxIndex.HasValue)
        {
            await smsService.DeleteMessageAsync(record.InboxIndex.Value, cancellationToken);
        }

        return NoContent();
    }

    [HttpGet]
    [Authorize(Policy = "MasterKeyOnly")]
    [ProducesResponseType(typeof(SmsDeviceInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SmsDeviceInfo>> DeviceInfo(CancellationToken cancellationToken)
    {
        var info = await smsService.GetDeviceInfoAsync(cancellationToken);
        return Ok(info);
    }

    [HttpPost("{id:guid}")]
    [ProducesResponseType(typeof(RetryWebhookSmsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(BadRequestErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(NotFoundErrorDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ForbiddenErrorDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(InternalServerErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RetryWebhookSmsResponseDto>> RetryWebhook([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var currentApiKeyId = TryGetApiKeyId();
        var isMasterKey = IsMasterKey();

        var record = await dbContext.SmsRecords
            .Where(x => x.Id == id && x.Direction == SmsRecordDirections.Incoming)
            .FirstOrDefaultAsync(cancellationToken);

        if (record is null)
        {
            return NotFound(new NotFoundErrorDto
            {
                Message = "SMS message not found.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        if (!isMasterKey && record.ApiKeyId != currentApiKeyId)
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(record.FromPhoneNumber))
        {
            return BadRequest(new BadRequestErrorDto
            {
                Message = "This message has no sender number to resolve a webhook for.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        // Slå aktiv subscription op på ny, så en netop rettet webhook-URL eller ejer bruges frem for de data der var gældende da SMS'en oprindeligt blev modtaget.
        var today = DateOnly.FromDateTime(DateTime.Now);
        var activeSubscription = await dbContext.SubscriptionNumbers
            .Where(sn =>
                sn.PhoneNumber == record.FromPhoneNumber &&
                sn.Subscription.StartDate <= today &&
                sn.Subscription.EndDate >= today)
            .Select(sn => new
            {
                sn.Subscription.ApiKeyId,
                sn.Subscription.WebhookUrl
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (activeSubscription is null || string.IsNullOrWhiteSpace(activeSubscription.WebhookUrl))
        {
            return BadRequest(new BadRequestErrorDto
            {
                Message = "No active subscription with a webhook URL is currently configured for this number.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        record.ApiKeyId = activeSubscription.ApiKeyId;

        var payload = new
        {
            originator = record.FromPhoneNumber,
            timestamp = record.ReceivedAt,
            body = record.Message
        };

        var result = await webhookSender.SendAsync(activeSubscription.WebhookUrl, payload, cancellationToken);

        if (result.Success)
        {
            // Webhook leveret – fjern fra DB (SMS'en er allerede slettet fra SIM-kortet ved modtagelse).
            dbContext.SmsRecords.Remove(record);
        }
        else
        {
            record.Status = SmsRecordStatuses.WebhookFailed;
            record.FailureReason = result.FailureReason;
            record.UpdatedAt = DateTime.Now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new RetryWebhookSmsResponseDto
        {
            MessageId = id,
            Delivered = result.Success,
            WebhookUrl = activeSubscription.WebhookUrl,
            FailureReason = result.FailureReason
        });
    }

    private static string NormalizePhone(string phone)
    {
        return phone.Trim().Replace(" ", string.Empty).Replace("-", string.Empty);
    }

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

    private static string NormalizePhoneForCompare(string phone)
    {
        return NormalizeDanishPhone(phone);
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