using System.Text.Json;
using System.Text.RegularExpressions;
using api.Data;
using api.Models;
using api.Services.Interfaces;
using api.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Services;

public sealed class SmsInboxWorker(
    IServiceScopeFactory serviceScopeFactory,
    IWebhookSender webhookSender,
    ILogger<SmsInboxWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
    private static readonly Regex DanishPhoneRegex = new("^[2-9][0-9]{7}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private const int StandardSmsLength = 160;
    private const string AutoReplyMessage = "Din besked er modtaget, men dit nummer er ikke registreret i et aktivt IT-system. Din besked vil ikke blive læst.";

    private DateTime _lastCleanup = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollInboxAsync(stoppingToken);

                if (DateTime.Now - _lastCleanup > CleanupInterval)
                {
                    await CleanupOldRecordsAsync(stoppingToken);
                    _lastCleanup = DateTime.Now;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in SmsInboxWorker.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task PollInboxAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var smsService = scope.ServiceProvider.GetRequiredService<ISmsService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        IReadOnlyList<SmsMessage> messages;
        try
        {
            messages = await smsService.ListMessagesAsync(SmsMessageStatus.All, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to list messages from modem.");
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);

        foreach (var message in messages)
        {
            var normalized = NormalizeDanishPhone(message.Originator ?? string.Empty);

            // Slet ikke-danske numre fra modemet
            if (!DanishPhoneRegex.IsMatch(normalized))
            {
                await TryDeleteFromModemAsync(smsService, message.Index, cancellationToken);
                continue;
            }

            // Tjek om beskeden allerede er persisteret
            var alreadyExists = await dbContext.SmsRecords.AnyAsync(
                x => x.Direction == SmsRecordDirections.Incoming
                     && x.InboxIndex == message.Index
                     && x.Message == message.Body,
                cancellationToken);

            if (alreadyExists)
            {
                continue;
            }

            // Find aktiv subscription for dette nummer
            var activeSubscription = await dbContext.SubscriptionNumbers
                .Where(sn =>
                    sn.PhoneNumber == normalized &&
                    sn.Subscription.StartDate <= today &&
                    sn.Subscription.EndDate >= today)
                .Select(sn => new
                {
                    sn.Subscription.ApiKeyId,
                    sn.Subscription.WebhookUrl
                })
                .FirstOrDefaultAsync(cancellationToken);

            // Nummeret tilhører ingen aktiv subscription: send auto-svar og slet fra modem uden at gemme i DB
            if (activeSubscription is null)
            {
                await TrySendAutoReplyAsync(smsService, normalized, dbContext, cancellationToken);
                await TryDeleteFromModemAsync(smsService, message.Index, cancellationToken);
                continue;
            }

            // Gem SMS i databasen
            var record = new SmsRecord
            {
                Id = Guid.NewGuid(),
                ApiKeyId = activeSubscription.ApiKeyId,
                Direction = SmsRecordDirections.Incoming,
                Status = SmsRecordStatuses.Received,
                ToPhoneNumber = string.Empty,
                FromPhoneNumber = normalized,
                Message = message.Body,
                UnitPriceDkk = 0m,
                SegmentCount = Math.Max(1, (int)Math.Ceiling(message.Body.Length / (decimal)StandardSmsLength)),
                TotalPriceDkk = 0m,
                IsBalanceDeducted = false,
                CreatedAt = DateTime.Now,
                ReceivedAt = message.Timestamp?.LocalDateTime ?? DateTime.Now,
                InboxIndex = message.Index,
                ModemResponseJson = JsonSerializer.Serialize(new[] { message.RawHeader, message.Body })
            };

            dbContext.SmsRecords.Add(record);
            await dbContext.SaveChangesAsync(cancellationToken);

            // SMS er nu i databasen – slet altid fra modem så SIM-kortet holdes tomt
            await TryDeleteFromModemAsync(smsService, message.Index, cancellationToken);

            // Send til webhook hvis URL er konfigureret
            if (!string.IsNullOrWhiteSpace(activeSubscription.WebhookUrl))
            {
                var payload = new
                {
                    index = message.Index,
                    status = message.Status,
                    originator = message.Originator,
                    timestamp = message.Timestamp,
                    body = message.Body
                };

                var result = await webhookSender.SendAsync(activeSubscription.WebhookUrl, payload, cancellationToken);
                if (result.Success)
                {
                    // SMS leveret via webhook – fjern fra DB
                    dbContext.SmsRecords.Remove(record);
                }
                else
                {
                    // Behold posten så den kan genudløses fra adm-modulet, når webhooket er rettet.
                    record.Status = SmsRecordStatuses.WebhookFailed;
                    record.FailureReason = result.FailureReason;
                    record.UpdatedAt = DateTime.Now;
                }

                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
    }

    private async Task TrySendAutoReplyAsync(ISmsService smsService, string toNumber, AppDbContext dbContext, CancellationToken cancellationToken)
    {
        try
        {
            await smsService.SendSmsToModemAsync(toNumber, AutoReplyMessage, cancellationToken);

            dbContext.SmsRecords.Add(new SmsRecord
            {
                Id = Guid.NewGuid(),
                Direction = SmsRecordDirections.Outgoing,
                Status = SmsRecordStatuses.Sent,
                ToPhoneNumber = toNumber,
                Message = AutoReplyMessage,
                UnitPriceDkk = 0m,
                SegmentCount = Math.Max(1, (int)Math.Ceiling(AutoReplyMessage.Length / (decimal)StandardSmsLength)),
                TotalPriceDkk = 0m,
                IsBalanceDeducted = false,
                CreatedAt = DateTime.Now,
                SentAt = DateTime.Now
            });

            // The auto-reply was already sent via the modem above, so a persistence failure here
            // only loses the local record of it — it must not be retried as a send.
            if (!await dbContext.TrySaveChangesWithRetryAsync(logger, maxAttempts: 5, cancellationToken))
            {
                logger.LogError("Auto-reply to {PhoneNumber} was sent via the modem but could not be persisted after retries.", toNumber);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send auto-reply to {PhoneNumber}.", toNumber);
        }
    }

    private async Task TryDeleteFromModemAsync(ISmsService smsService, int index, CancellationToken cancellationToken)
    {
        try
        {
            await smsService.DeleteMessageAsync(index, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete message at index {Index} from modem.", index);
        }
    }

    private async Task CleanupOldRecordsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var cutoff = DateTime.Now.AddDays(-30);
            var old = await dbContext.SmsRecords
                .Where(x => x.CreatedAt < cutoff)
                .ToListAsync(cancellationToken);

            if (old.Count > 0)
            {
                dbContext.SmsRecords.RemoveRange(old);
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Cleaned up {Count} SMS records older than 30 days.", old.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to clean up old SMS records.");
        }
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
}
