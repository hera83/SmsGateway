using System.Text.Json;
using api.Data;
using api.Models;
using api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace api.Services;

public sealed class SmsQueueWorker(IServiceScopeFactory serviceScopeFactory, ILogger<SmsQueueWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var hadWork = await ProcessNextAsync(stoppingToken);
                if (!hadWork)
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in SMS queue worker.");
                await Task.Delay(IdleDelay, stoppingToken);
            }
        }
    }

    private async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sms = await dbContext.SmsRecords
            .Where(x => x.Direction == SmsRecordDirections.Outgoing && x.Status == SmsRecordStatuses.Queued)
            .OrderBy(x => x.QueuedAt)
            .ThenBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (sms is null)
        {
            return false;
        }

        sms.Status = SmsRecordStatuses.Sending;
        sms.ProcessingStartedAt = DateTime.Now;
        sms.UpdatedAt = DateTime.Now;

        if (!await dbContext.TrySaveChangesWithRetryAsync(logger, maxAttempts: 3, cancellationToken))
        {
            // Nothing was sent yet, so the record is safely left as Queued and picked up again next poll.
            logger.LogError("Could not mark SMS {SmsId} as Sending; it will be retried on the next poll.", sms.Id);
            return true;
        }

        var smsService = scope.ServiceProvider.GetRequiredService<ISmsService>();

        try
        {
            var sendResult = await smsService.SendSmsToModemAsync(sms.ToPhoneNumber, sms.Message, cancellationToken);

            sms.Status = SmsRecordStatuses.Sent;
            sms.SentAt = DateTime.Now;
            sms.ModemReference = sendResult.ModemReference;
            sms.ModemResponseJson = JsonSerializer.Serialize(sendResult.ModemResponse);
            sms.FailureReason = null;

            if (!sms.IsBalanceDeducted && sms.ApiKeyId.HasValue)
            {
                var apiKey = await dbContext.ApiKeys.FirstOrDefaultAsync(x => x.Id == sms.ApiKeyId.Value, cancellationToken);
                if (apiKey is not null)
                {
                    apiKey.Balance -= sms.TotalPriceDkk;
                    apiKey.UpdatedAt = DateTime.Now;
                    sms.IsBalanceDeducted = true;
                }
            }

            sms.UpdatedAt = DateTime.Now;

            // The modem call above already succeeded — the SMS is out. A failure to persist that
            // fact must never fall into the catch block below and get recorded as Failed, or the
            // caller will see a false failure, resend, and the recipient gets the message twice.
            if (!await dbContext.TrySaveChangesWithRetryAsync(logger, maxAttempts: 5, cancellationToken))
            {
                logger.LogCritical(
                    "SMS {SmsId} was sent to the modem (reference {ModemReference}) but the Sent status could not be persisted after retries. Record remains {Status} in the database and needs manual reconciliation.",
                    sms.Id, sms.ModemReference, SmsRecordStatuses.Sending);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send queued SMS {SmsId}", sms.Id);

            sms.Status = SmsRecordStatuses.Failed;
            sms.FailedAt = DateTime.Now;
            sms.FailureReason = ex.Message;
            sms.UpdatedAt = DateTime.Now;
            await dbContext.TrySaveChangesWithRetryAsync(logger, maxAttempts: 5, cancellationToken);
        }

        return true;
    }
}
