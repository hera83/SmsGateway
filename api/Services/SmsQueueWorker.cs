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
        await dbContext.SaveChangesAsync(cancellationToken);

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
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send queued SMS {SmsId}", sms.Id);

            sms.Status = SmsRecordStatuses.Failed;
            sms.FailedAt = DateTime.Now;
            sms.FailureReason = ex.Message;
            sms.UpdatedAt = DateTime.Now;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }
}
