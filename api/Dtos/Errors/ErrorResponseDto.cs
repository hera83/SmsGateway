namespace api.Dtos.Errors;

public class ErrorResponseDto
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int Status { get; set; }
    public string? TraceId { get; set; }
}
