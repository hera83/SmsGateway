namespace api.Dtos.Errors;

public sealed class ValidationErrorDto : ErrorResponseDto
{
    public Dictionary<string, string[]> Errors { get; set; } = new();

    public ValidationErrorDto()
    {
        Code = "validation_failed";
        Status = StatusCodes.Status400BadRequest;
    }
}
