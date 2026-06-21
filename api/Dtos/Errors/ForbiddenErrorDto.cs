namespace api.Dtos.Errors;

public sealed class ForbiddenErrorDto : ErrorResponseDto
{
    public ForbiddenErrorDto()
    {
        Code = "forbidden";
        Status = StatusCodes.Status403Forbidden;
    }
}
