namespace api.Models;

public class LogEntry
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? MessageTemplate { get; set; }
    public string? Exception { get; set; }
    public string? SourceContext { get; set; }
    public string? PropertiesJson { get; set; }
}
