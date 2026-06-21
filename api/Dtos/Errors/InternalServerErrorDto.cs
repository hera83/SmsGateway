namespace api.Dtos.Errors;

public sealed class InternalServerErrorDto : ErrorResponseDto
{
    public InternalServerErrorDto()
    {
        Code = "internal_server_error";
        Status = StatusCodes.Status500InternalServerError;
    }
}
