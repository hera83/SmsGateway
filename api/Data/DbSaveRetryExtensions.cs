using Microsoft.EntityFrameworkCore;

namespace api.Data;

public static class DbSaveRetryExtensions
{
    /// <summary>
    /// Saves changes, retrying transient failures (e.g. SQLite "database is locked") with backoff.
    /// Never throws on <see cref="DbUpdateException"/> — returns false so the caller can decide how
    /// to represent an outcome that already happened (e.g. a modem send) but couldn't be persisted,
    /// instead of the exception forcing that outcome to be mislabeled as failed.
    /// </summary>
    public static async Task<bool> TrySaveChangesWithRetryAsync(
        this AppDbContext dbContext,
        ILogger logger,
        int maxAttempts = 5,
        CancellationToken cancellationToken = default)
    {
        var delay = TimeSpan.FromMilliseconds(200);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException ex)
            {
                if (attempt == maxAttempts)
                {
                    logger.LogError(ex, "SaveChangesAsync failed after {MaxAttempts} attempts.", maxAttempts);
                    return false;
                }

                logger.LogWarning(ex, "SaveChangesAsync failed (attempt {Attempt}/{MaxAttempts}), retrying.", attempt, maxAttempts);
                await Task.Delay(delay, cancellationToken);
                delay *= 2;
            }
        }

        return false;
    }
}
