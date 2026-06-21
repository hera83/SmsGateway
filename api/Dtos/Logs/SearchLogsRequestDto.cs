namespace api.Dtos.Logs;

public sealed class SearchLogsRequestDto
{
    public string? Level { get; set; }
    public string? Q { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
