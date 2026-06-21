namespace api.Dtos.Logs;

public sealed class SearchLogsResponseDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public List<SearchLogsItemResponseDto> Items { get; set; } = new();
}
