namespace api.Dtos.Errors;

public sealed class UnauthorizedErrorDto : ErrorResponseDto
{
    public UnauthorizedErrorDto()
    {
        Code = "unauthorized";
        Status = StatusCodes.Status401Unauthorized;
    }
}
