using web.Services.SmsService.Dtos.Logs;

namespace web.Models.Logs;

public sealed class LogsIndexViewModel
{
    public string? Level { get; set; }
    public string? Q { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;

    public SearchLogsResponseDto? Result { get; set; }
}
