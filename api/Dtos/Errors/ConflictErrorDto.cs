namespace api.Dtos.Errors;

public sealed class ConflictErrorDto : ErrorResponseDto
{
    public ConflictErrorDto()
    {
        Code = "conflict";
        Status = StatusCodes.Status409Conflict;
    }
}
