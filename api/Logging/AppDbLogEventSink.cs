using System.Text.Json;
using api.Data;
using api.Models;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace api.Logging;

public class AppDbLogEventSink : ILogEventSink
{
    [ThreadStatic]
    private static bool _isEmitting;

    private readonly IServiceScopeFactory _scopeFactory;

    public AppDbLogEventSink(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public void Emit(LogEvent logEvent)
    {
        if (_isEmitting)
        {
            return;
        }

        try
        {
            _isEmitting = true;

            using var scope = _scopeFactory.CreateScope();
            using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var sourceContext = logEvent.Properties.TryGetValue("SourceContext", out var source)
                ? source.ToString().Trim('"')
                : null;

            if (!string.IsNullOrWhiteSpace(sourceContext) && sourceContext.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            {
                return;
            }

            var entry = new LogEntry
            {
                Timestamp = logEvent.Timestamp.LocalDateTime,
                Level = logEvent.Level.ToString(),
                Message = logEvent.RenderMessage(),
                MessageTemplate = logEvent.MessageTemplate.Text,
                Exception = logEvent.Exception?.ToString(),
                SourceContext = sourceContext,
                PropertiesJson = SerializeProperties(logEvent)
            };

            db.LogEntries.Add(entry);
            db.SaveChanges();
        }
        catch
        {
        }
        finally
        {
            _isEmitting = false;
        }
    }

    private static string SerializeProperties(LogEvent logEvent)
    {
        var values = logEvent.Properties.ToDictionary(
            x => x.Key,
            x => x.Value.ToString());

        return JsonSerializer.Serialize(values);
    }
}
